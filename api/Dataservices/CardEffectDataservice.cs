using System;
using System.Collections.Generic;
using System.Linq;

namespace Homo.Bet.Api
{
    public static class CARD_NAME
    {
        public const string EQUALIZE_WEALTH = "均富卡";
        public const string DOUBLE_EARNINGS = "收益加倍";
    }

    public class CardEffectResult
    {
        public string Effect { get; set; }
        public int TargetQty { get; set; }
        public List<CardEffectAdjustment> Adjustments { get; set; } = new List<CardEffectAdjustment>();
    }

    public class CardEffectAdjustment
    {
        public long UserId { get; set; }
        public int Before { get; set; }
        public int Added { get; set; }
    }

    public class CardEffectDataservice
    {
        // 與 cardOwner 同屬任一組織的所有使用者（含自己）
        public static List<long> GetOrganizationMateIds(BargainingChipDBContext dbContext, long userId)
        {
            var organizationIds = dbContext.RelationOfOrganizationAndUser
                .Where(x => x.DeletedAt == null && x.UserId == userId)
                .Select(x => x.OrganizationId)
                .ToList();

            var mateIds = dbContext.RelationOfOrganizationAndUser
                .Where(x => x.DeletedAt == null && organizationIds.Contains(x.OrganizationId))
                .Select(x => x.UserId)
                .Distinct()
                .ToList();

            if (!mateIds.Contains(userId))
            {
                mateIds.Add(userId);
            }
            return mateIds;
        }

        // 均富卡：讓組織內所有人的 coins 補到跟組織內最多的人一樣
        public static CardEffectResult EqualizeWealth(BargainingChipDBContext dbContext, long cardOwnerId)
        {
            var mateIds = GetOrganizationMateIds(dbContext, cardOwnerId);
            var balances = CoinsLogDataService.GetEarnCoinsByOwners(dbContext, mateIds);
            int max = balances.Values.DefaultIfEmpty(0).Max();

            var result = new CardEffectResult() { Effect = CARD_NAME.EQUALIZE_WEALTH, TargetQty = max };
            foreach (var userId in mateIds)
            {
                int before = balances[userId];
                int diff = max - before;
                if (diff <= 0)
                {
                    continue;
                }
                dbContext.CoinLog.Add(new CoinLog()
                {
                    CreatedAt = DateTime.Now,
                    CreatedBy = cardOwnerId,
                    OwnerId = userId,
                    Type = COIN_LOG_TYPE.EARN,
                    Qty = diff
                });
                result.Adjustments.Add(new CardEffectAdjustment() { UserId = userId, Before = before, Added = diff });
            }
            dbContext.SaveChanges();
            return result;
        }

        // 收益加倍：裝備中（未過期）的「收益加倍」增益卡，任務收益 x2
        public static int GetEarnMultiplier(BargainingChipDBContext dbContext, long userId)
        {
            bool hasDoubleEarnings = dbContext.UserCard
                .Any(x => x.DeletedAt == null
                    && x.UserId == userId
                    && x.IsEquipped == true
                    && x.Card.Type == CARD_TYPE.BUFF
                    && x.Card.Name == CARD_NAME.DOUBLE_EARNINGS);
            return hasDoubleEarnings ? 2 : 1;
        }
    }
}
