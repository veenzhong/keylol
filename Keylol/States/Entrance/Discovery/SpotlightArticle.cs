﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Keylol.Identity;
using Keylol.Models;
using Keylol.Models.DAL;
using Keylol.Provider.CachedDataProvider;
using Keylol.StateTreeManager;
using Keylol.Utilities;
using Newtonsoft.Json;

namespace Keylol.States.Entrance.Discovery
{
    /// <summary>
    /// 精选文章列表
    /// </summary>
    public class SpotlightArticleList : List<SpotlightArticle>
    {
        private SpotlightArticleList(int capacity) : base(capacity)
        {
        }

        /// <summary>
        /// 创建 <see cref="SpotlightArticleList"/>
        /// </summary>
        /// <param name="currentUserId">当前登录用户 ID</param>
        /// <param name="page">分页页码</param>
        /// <param name="recordsPerPage">每页数量</param>
        /// <param name="spotlightArticleCategory">文章分类</param>
        /// <param name="dbContext"><see cref="KeylolDbContext"/></param>
        /// <param name="cachedData"><see cref="CachedDataProvider"/></param>
        /// <returns><see cref="SpotlightArticleList"/></returns>
        public static async Task<SpotlightArticleList> CreateAsync(string currentUserId, int page, int recordsPerPage,
            SpotlightArticleStream.ArticleCategory spotlightArticleCategory, KeylolDbContext dbContext,
            CachedDataProvider cachedData)
        {
            var streamName = SpotlightArticleStream.Name(spotlightArticleCategory);
            var queryResult = await (from feed in dbContext.Feeds
                where feed.StreamName == streamName
                join article in dbContext.Articles on feed.Entry equals article.Id
                where article.Rejected == false && article.Archived == ArchivedState.None
                orderby feed.Id descending
                select new
                {
                    article.Id,
                    FeedId = feed.Id,
                    FeedProperties = feed.Properties,
                    article.AuthorId,
                    AuthorIdCode = article.Author.IdCode,
                    AuthorAvatarImage = article.Author.AvatarImage,
                    AuthorUserName = article.Author.UserName,
                    article.PublishTime,
                    article.SidForAuthor,
                    article.Title,
                    article.Subtitle,
                    article.CoverImage,
                    PointIdCode = article.TargetPoint.IdCode,
                    PointAvatarImage = article.TargetPoint.AvatarImage,
                    PointType = article.TargetPoint.Type,
                    PointChineseName = article.TargetPoint.ChineseName,
                    PointEnglishName = article.TargetPoint.EnglishName,
                    PointSteamAppId = article.TargetPoint.SteamAppId
                })
                .TakePage(page, recordsPerPage)
                .ToListAsync();
            var result = new SpotlightArticleList(queryResult.Count);
            foreach (var a in queryResult)
            {
                var p = JsonConvert.DeserializeObject<SpotlightArticleStream.FeedProperties>(a.FeedProperties);
                result.Add(new SpotlightArticle
                {
                    Id = a.Id,
                    FeedId = a.FeedId,
                    AuthorIdCode = a.AuthorIdCode,
                    AuthorAvatarImage = a.AuthorAvatarImage,
                    AuthorUserName = a.AuthorUserName,
                    AuthorIsFriend = string.IsNullOrWhiteSpace(currentUserId)
                        ? (bool?) null
                        : await cachedData.Users.IsFriendAsync(currentUserId, a.AuthorId),
                    PublishTime = a.PublishTime,
                    SidForAuthor = a.SidForAuthor,
                    Title = p?.Title ?? a.Title,
                    Subtitle = p?.Subtitle ?? a.Subtitle,
                    CoverImage = a.CoverImage,
                    PointIdCode = a.PointIdCode,
                    PointAvatarImage = a.PointAvatarImage,
                    PointType = a.PointType,
                    PointChineseName = a.PointChineseName,
                    PointEnglishName = a.PointEnglishName,
                    PointInLibrary = string.IsNullOrWhiteSpace(currentUserId) || a.PointSteamAppId == null
                        ? (bool?) null
                        : await cachedData.Users.IsSteamAppInLibraryAsync(currentUserId, a.PointSteamAppId.Value)
                });
            }
            return result;
        }

        /// <summary>
        /// 获取推送参考
        /// </summary>
        /// <param name="link">文章链接</param>
        /// <param name="dbContext"><see cref="KeylolDbContext"/></param>
        /// <returns><see cref="SpotlightArticle"/></returns>
        public static async Task<SpotlightArticle> GetReference(string link, [Injected] KeylolDbContext dbContext)
        {
            var result = new SpotlightArticle();
            var match = Regex.Match(link, @"^https?:\/\/.+\.keylol\.com(?::\d+)?\/article\/(.+)\/(\d+)$");
            if (!match.Success)
                return result;
            var idCode = match.Groups[1].Value;
            var sidForAuthor = int.Parse(match.Groups[2].Value);
            var article = await dbContext.Articles
                .Where(a => a.Author.IdCode == idCode && a.SidForAuthor == sidForAuthor)
                .Select(a => new
                {
                    a.Id,
                    a.Title,
                    a.Subtitle,
                    AuthorIdCode = a.Author.IdCode,
                    PointIdCode = a.TargetPoint.IdCode
                })
                .SingleOrDefaultAsync();
            if (article == null)
                return result;
            result.Id = article.Id;
            result.Title = article.Title;
            result.Subtitle = article.Subtitle;
            result.AuthorIdCode = article.AuthorIdCode;
            result.PointIdCode = article.PointIdCode;
            return result;
        }

        /// <summary>
        /// 推送参考
        /// </summary>
        [Authorize(Roles = KeylolRoles.Operator)]
        public SpotlightArticle Reference { get; set; }
    }

    /// <summary>
    /// 精选文章
    /// </summary>
    public class SpotlightArticle
    {
        /// <summary>
        /// ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Feed ID
        /// </summary>
        public long? FeedId { get; set; }

        /// <summary>
        /// 作者识别码
        /// </summary>
        public string AuthorIdCode { get; set; }

        /// <summary>
        /// 作者头像
        /// </summary>
        public string AuthorAvatarImage { get; set; }

        /// <summary>
        /// 作者昵称
        /// </summary>
        public string AuthorUserName { get; set; }

        /// <summary>
        /// 作者是否是当前登录用户的好友
        /// </summary>
        public bool? AuthorIsFriend { get; set; }

        /// <summary>
        /// 发布时间
        /// </summary>
        public DateTime? PublishTime { get; set; }

        /// <summary>
        /// 作者名下序号
        /// </summary>
        public int? SidForAuthor { get; set; }

        /// <summary>
        /// 标题
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 副标题
        /// </summary>
        public string Subtitle { get; set; }

        /// <summary>
        /// 封面图
        /// </summary>
        public string CoverImage { get; set; }

        /// <summary>
        /// 收稿据点识别码
        /// </summary>
        public string PointIdCode { get; set; }

        /// <summary>
        /// 收稿据点头像
        /// </summary>
        public string PointAvatarImage { get; set; }

        /// <summary>
        /// 收稿据点类型
        /// </summary>
        public PointType? PointType { get; set; }

        /// <summary>
        /// 收稿据点中文名
        /// </summary>
        public string PointChineseName { get; set; }

        /// <summary>
        /// 收稿据点英文名
        /// </summary>
        public string PointEnglishName { get; set; }

        /// <summary>
        /// 收稿据点是否已入库
        /// </summary>
        public bool? PointInLibrary { get; set; }
    }
}