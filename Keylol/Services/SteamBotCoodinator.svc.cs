﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using DevTrends.WCFDataAnnotations;
using Keylol.Hubs;
using Keylol.Models.DAL;
using Keylol.Models.DTO;
using Keylol.Services.Contracts;
using Keylol.Utilities;
using Microsoft.AspNet.SignalR;
using StatusClaim = Keylol.Services.Contracts.StatusClaim;

namespace Keylol.Services
{
    [ValidateDataAnnotationsBehavior]
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession)]
    public class SteamBotCoodinator : ISteamBotCoodinator
    {
        private readonly string _sessionId = OperationContext.Current.SessionId;
        private bool _botAllocated;

        public SteamBotCoodinator()
        {
            OperationContext.Current.Channel.Closed += OnClientClosed;
            if (_sessionId != null)
            {
                Clients[_sessionId] =
                    OperationContext.Current.GetCallbackChannel<ISteamBotCoodinatorCallback>();
            }
            using (var dbContext = new KeylolDbContext())
            {
                foreach (var bot in dbContext.SteamBots.Where(bot => !Clients.Keys.Contains(bot.SessionId)))
                {
                    bot.SessionId = null;
                }
                dbContext.SaveChanges();
            }
        }

        public static ConcurrentDictionary<string, ISteamBotCoodinatorCallback> Clients { get; } =
            new ConcurrentDictionary<string, ISteamBotCoodinatorCallback>();

        public async Task<IEnumerable<SteamBotDto>> AllocateBots()
        {
            if (_botAllocated)
            {
                throw new FaultException("Only one allocation can be performed per session.");
            }
            _botAllocated = true;

            using (var dbContext = new KeylolDbContext())
            {
                var bots = await dbContext.SteamBots.Where(bot => bot.SessionId == null && bot.Enabled)
                    .Take(() => 15)
                    .ToListAsync();
                foreach (var bot in bots)
                {
                    bot.Online = false;
                    bot.SessionId = _sessionId;
                }
                await dbContext.SaveChangesAsync();
                return bots.Select(bot => new SteamBotDto(bot, true));
            }
        }

        public async Task UpdateBots(IList<SteamBotUpdateRequestDto> vms)
        {
            using (var dbContext = new KeylolDbContext())
            {
                var ids = vms.Select(vm => vm.Id);
                var bots =
                    await
                        dbContext.SteamBots.Where(bot => ids.Contains(bot.Id))
                            .ToListAsync();
                for (var i = 0; i < bots.Count; i++)
                {
                    if (vms[i].SteamId != null)
                        bots[i].SteamId = vms[i].SteamId;
                    if (vms[i].FriendCount != null)
                        bots[i].FriendCount = vms[i].FriendCount.Value;
                    if (vms[i].Online != null)
                        bots[i].Online = vms[i].Online.Value;
                }
                await dbContext.SaveChangesAsync();
            }
        }

        public async Task<UserDto> GetUserBySteamId(string steamId)
        {
            using (var dbContext = new KeylolDbContext())
            {
                var user =
                    await dbContext.Users.Include(u => u.SteamBot).SingleOrDefaultAsync(u => u.SteamId == steamId);
                return user == null ? null : new UserDto(user, true, true) {SteamBot = new SteamBotDto(user.SteamBot)};
            }
        }

        public async Task<IList<UserDto>> GetUsersBySteamIds(IList<string> steamIds)
        {
            using (var dbContext = new KeylolDbContext())
            {
                var users =
                    await
                        dbContext.Users.Include(u => u.SteamBot).Where(u => steamIds.Contains(u.SteamId)).ToListAsync();
                return
                    users.Select(user => new UserDto(user, true, true) {SteamBot = new SteamBotDto(user.SteamBot)})
                        .ToList();
            }
        }

        public async Task SetUserStatus(string steamId, StatusClaim status)
        {
            using (var dbContext = new KeylolDbContext())
            {
                var user = await dbContext.Users.SingleOrDefaultAsync(u => u.SteamId == steamId);
                if (user != null)
                {
                    var userManager = KeylolUserManager.Create(dbContext);
                    switch (status)
                    {
                        case StatusClaim.Normal:
                            await userManager.RemoveStatusClaimAsync(user.Id);
                            break;

                        case StatusClaim.Probationer:
                            await userManager.SetStatusClaimAsync(user.Id, Utilities.StatusClaim.Probationer);
                            break;
                    }
                }
            }
        }

        public async Task SetUserSteamProfileName(string steamId, string name)
        {
            using (var dbContext = new KeylolDbContext())
            {
                var user = await dbContext.Users.SingleOrDefaultAsync(u => u.SteamId == steamId);
                if (user != null)
                {
                    user.SteamProfileName = name;
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        public async Task DeleteBindingToken(string botId, string steamId)
        {
            using (var dbContext = new KeylolDbContext())
            {
                var tokens =
                    await
                        dbContext.SteamBindingTokens.Where(t => t.SteamId == steamId && t.BotId == botId).ToListAsync();
                dbContext.SteamBindingTokens.RemoveRange(tokens);
                await dbContext.SaveChangesAsync();
            }
        }

        public Task<string> GetCMServer()
        {
            return Task.FromResult("58.215.54.121:27018");
        }

        public async Task<bool> BindSteamUserWithBindingToken(string code, string botId, string userSteamId,
            string userSteamProfileName, string userSteamAvatarHash)
        {
            using (var dbContext = new KeylolDbContext())
            {
                var token =
                    await
                        dbContext.SteamBindingTokens.SingleOrDefaultAsync(
                            t => t.Code == code && t.BotId == botId && t.SteamId == null);
                if (token == null)
                    return false;

                token.SteamId = userSteamId;
                await dbContext.SaveChangesAsync();
                GlobalHost.ConnectionManager.GetHubContext<SteamBindingHub, ISteamBindingHubClient>()
                    .Clients.Client(token.BrowserConnectionId)?
                    .NotifyCodeReceived(userSteamProfileName, userSteamAvatarHash);
                return true;
            }
        }

        public async Task<bool> BindSteamUserWithLoginToken(string userSteamId, string code)
        {
            using (var dbContext = new KeylolDbContext())
            {
                var token =
                    await dbContext.SteamLoginTokens.SingleOrDefaultAsync(t => t.Code == code);
                if (token == null)
                    return false;

                token.SteamId = userSteamId;
                await dbContext.SaveChangesAsync();
                GlobalHost.ConnectionManager.GetHubContext<SteamLoginHub, ISteamLoginHubClient>()
                    .Clients.Client(token.BrowserConnectionId)?
                    .NotifyCodeReceived();
                return true;
            }
        }

        public async Task BroadcastBotOnFriendAdded(string botId)
        {
            using (var dbContext = new KeylolDbContext())
            {
                GlobalHost.ConnectionManager.GetHubContext<SteamBindingHub, ISteamBindingHubClient>()
                    .Clients.Clients(
                        await dbContext.SteamBots.Where(bot => bot.Id == botId)
                            .SelectMany(bot => bot.BindingTokens)
                            .Select(token => token.BrowserConnectionId)
                            .ToListAsync()
                    )?
                    .NotifySteamFriendAdded();
            }
        }

        private async void OnClientClosed(object sender, EventArgs eventArgs)
        {
            ISteamBotCoodinatorCallback callback;
            Clients.TryRemove(_sessionId, out callback);
            using (var dbContext = new KeylolDbContext())
            {
                var bots =
                    await
                        dbContext.SteamBots.Where(bot => bot.SessionId == _sessionId).ToListAsync();
                foreach (var bot in bots)
                {
                    bot.SessionId = null;
                }
                await dbContext.SaveChangesAsync();
            }
        }
    }
}