﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using Keylol.Controllers.User;
using Keylol.Models;
using Keylol.Models.DTO;
using Keylol.Provider;
using Keylol.Utilities;

namespace Keylol.Controllers.Article
{
    public partial class ArticleController
    {
        /// <summary>
        ///     获取指定用户时间轴的文章
        /// </summary>
        /// <param name="userId">用户 ID</param>
        /// <param name="idType">ID 类型，默认 "Id"</param>
        /// <param name="articleTypeFilter">文章类型过滤器，用逗号分个多个类型的名字，null 表示全部类型，默认 null</param>
        /// <param name="source">来源过滤器，1 表示发表的文章，2 表示认可的文章，默认 1</param>
        /// <param name="beforeSN">获取编号小于这个数字的文章，用于分块加载，默认 2147483647</param>
        /// <param name="take">获取数量，最大 50，默认 30</param>
        [Route("user/{userId}")]
        [AllowAnonymous]
        [HttpGet]
        [ResponseType(typeof (List<ArticleDTO>))]
        public async Task<IHttpActionResult> GetListByUser(string userId, UserController.IdType idType,
            string articleTypeFilter = null, int source = 1, int beforeSN = int.MaxValue, int take = 30)
        {
            KeylolUser user;
            switch (idType)
            {
                case UserController.IdType.Id:
                    user = await DbContext.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
                    break;

                case UserController.IdType.IdCode:
                    user = await DbContext.Users.AsNoTracking().SingleAsync(u => u.IdCode == userId);
                    break;

                case UserController.IdType.UserName:
                    user = await DbContext.Users.AsNoTracking().SingleAsync(u => u.UserName == userId);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(idType), idType, null);
            }

            if (take > 50) take = 50;
            var userQuery = DbContext.Users.AsNoTracking().Where(u => u.Id == user.Id);
            var publishedQuery = userQuery.SelectMany(u => u.ProfilePoint.Articles)
                .Where(a => a.SequenceNumber < beforeSN)
                .Select(a => new
                {
                    article = a,
                    reason = ArticleDTO.TimelineReasonType.Publish,
                    author = (KeylolUser) null
                });
            var likedQuery = userQuery.SelectMany(u => u.Likes.OfType<ArticleLike>())
                .Where(l => l.Backout == false && l.Article.SequenceNumber < beforeSN)
                .Select(l => new
                {
                    article = l.Article,
                    reason = ArticleDTO.TimelineReasonType.Like,
                    author = l.Article.Principal.User
                });
            var published = (source & 1) != 0;
            var liked = (source & 1 << 1) != 0;
            var articleQuery = publishedQuery;
            if (published)
            {
                if (liked)
                    articleQuery = articleQuery.Concat(likedQuery);
            }
            else
            {
                if (liked)
                    articleQuery = likedQuery;
                else
                    return Ok();
            }
            if (articleTypeFilter != null)
            {
                var types = articleTypeFilter.Split(',').Select(s => s.Trim().ToEnum<ArticleType>()).ToList();
                articleQuery = articleQuery.Where(PredicateBuilder.Contains(types, a => a.article.Type, new
                {
                    article = (Models.Article) null,
                    reason = ArticleDTO.TimelineReasonType.Like,
                    author = (KeylolUser) null
                }));
            }
            var articleEntries = await articleQuery.GroupBy(e => e.article)
                .OrderByDescending(g => g.Key.SequenceNumber).Take(() => take)
                .Select(g => new
                {
                    article = g.Key,
                    candicates = g,
                    reason = g.Max(ee => ee.reason)
                })
                .Select(g => new
                {
                    g.article,
                    g.reason,
                    g.candicates.FirstOrDefault(e => e.reason == g.reason).author,
                    voteForPoint = g.article.VoteForPoint,
                    likeCount = g.article.Likes.Count(l => l.Backout == false),
                    commentCount = g.article.Comments.Count,
                    type = g.article.Type
                })
                .ToListAsync();
            return Ok(articleEntries.Select(entry =>
            {
                var articleDto = new ArticleDTO(entry.article, true, 256, true)
                {
                    TimelineReason = entry.reason,
                    LikeCount = entry.likeCount,
                    CommentCount = entry.commentCount,
                    TypeName = entry.type.ToString(),
                    VoteForPoint = entry.voteForPoint == null ? null : new NormalPointDTO(entry.voteForPoint, true)
                };
                if (string.IsNullOrEmpty(entry.article.ThumbnailImage))
                {
                    articleDto.ThumbnailImage = entry.voteForPoint?.BackgroundImage;
                }
                if (articleDto.TypeName != "简评")
                    articleDto.TruncateContent(128);
                if (entry.reason != ArticleDTO.TimelineReasonType.Publish)
                {
                    articleDto.Author = new UserDTO(entry.author);
                }
                return articleDto;
            }).ToList());
        }
    }
}