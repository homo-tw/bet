using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Homo.Bet.Api
{
    public class CardRepository
    {
        private readonly BargainingChipDBContext _dbContext;

        public CardRepository(BargainingChipDBContext dbContext)
        {
            _dbContext = dbContext;
        }

        // 取得所有可用的卡片（商店卡片）
        public List<Card> GetAvailableCards()
        {
            return _dbContext.Card
                .Where(x => x.DeletedAt == null && x.IsAvailable == true)
                .OrderBy(x => x.Id)
                .ToList();
        }

        // 取得單一卡片
        public Card GetCardById(long cardId)
        {
            return _dbContext.Card
                .Where(x => x.DeletedAt == null && x.Id == cardId)
                .FirstOrDefault();
        }

        // 購買卡片（建立 UserCard 紀錄 + Soft Delete Card）
        public UserCard BuyCard(long userId, long cardId)
        {
            // 建立 UserCard 紀錄
            UserCard record = new UserCard();
            record.UserId = userId;
            record.CardId = cardId;
            record.IsEquipped = false;
            record.CreatedAt = DateTime.Now;
            _dbContext.UserCard.Add(record);

            // Soft Delete Card（標記為已售出，不再出現在商店）
            var card = _dbContext.Card.FirstOrDefault(x => x.Id == cardId);
            if (card != null)
            {
                card.DeletedAt = DateTime.Now;
            }

            _dbContext.SaveChanges();
            return record;
        }

        // 取得使用者擁有的所有卡片
        public List<UserCard> GetUserCards(long userId)
        {
            return _dbContext.UserCard
                .Include(x => x.Card)
                .Where(x => x.DeletedAt == null && x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }

        // 取得使用者已裝備的卡片
        public List<UserCard> GetEquippedCards(long userId)
        {
            return _dbContext.UserCard
                .Include(x => x.Card)
                .Where(x => x.DeletedAt == null && x.UserId == userId && x.IsEquipped == true)
                .OrderBy(x => x.EquippedAt)
                .ToList();
        }

        // 裝備卡片
        public UserCard EquipCard(long userId, long userCardId, string triggerCondition = null)
        {
            var userCard = _dbContext.UserCard
                .Include(x => x.Card)
                .Where(x => x.DeletedAt == null && x.Id == userCardId && x.UserId == userId)
                .FirstOrDefault();

            if (userCard == null) return null;

            // 檢查陷阱卡是否在週一裝備
            if (userCard.Card.Type == CARD_TYPE.TRAP)
            {
                // 檢查今天是否為週一
                if (DateTime.Now.DayOfWeek != DayOfWeek.Monday)
                {
                    return null;  // 陷阱卡只能在週一裝備
                }

                // 陷阱卡必須填寫觸發條件
                if (string.IsNullOrWhiteSpace(triggerCondition))
                {
                    return null;
                }
            }

            // 檢查是否已達裝備上限（3張）
            var equippedCount = _dbContext.UserCard
                .Count(x => x.DeletedAt == null && x.UserId == userId && x.IsEquipped == true);

            if (equippedCount >= 3) return null;

            userCard.IsEquipped = true;
            userCard.EquippedAt = DateTime.Now;
            userCard.TriggerCondition = triggerCondition;
            _dbContext.SaveChanges();
            return userCard;
        }

        // 卸下卡片
        public UserCard UnequipCard(long userId, long userCardId)
        {
            var userCard = _dbContext.UserCard
                .Where(x => x.DeletedAt == null && x.Id == userCardId && x.UserId == userId)
                .FirstOrDefault();

            if (userCard == null) return null;

            userCard.IsEquipped = false;
            userCard.EquippedAt = null;
            _dbContext.SaveChanges();
            return userCard;
        }

        // 使用卡片（僅限魔法卡，使用後立即消失）
        public UserCard UseCard(long userId, long userCardId, out CardEffectResult effect)
        {
            effect = null;
            var userCard = _dbContext.UserCard
                .Include(x => x.Card)
                .Where(x => x.DeletedAt == null && x.Id == userCardId && x.UserId == userId)
                .FirstOrDefault();

            if (userCard == null) return null;

            // 只有魔法卡可以使用（陷阱卡和增益卡是裝備）
            if (userCard.Card.Type != CARD_TYPE.MAGIC) return null;

            using (var transaction = _dbContext.Database.BeginTransaction())
            {
                // 使用後立即 soft delete
                userCard.DeletedAt = DateTime.Now;
                _dbContext.SaveChanges();

                if (userCard.Card.Name == CARD_NAME.EQUALIZE_WEALTH)
                {
                    effect = CardEffectDataservice.EqualizeWealth(_dbContext, userId);
                }

                transaction.Commit();
            }
            return userCard;
        }

        // 建立卡片（管理員用）
        public Card CreateCard(DTOs.Card dto)
        {
            Card record = new Card();
            record.Name = dto.Name;
            record.Type = dto.Type;
            record.Description = dto.Description;
            record.Cost = dto.Cost;
            record.IsAvailable = true;
            record.CreatedAt = DateTime.Now;
            _dbContext.Card.Add(record);
            _dbContext.SaveChanges();
            return record;
        }

        // 移除過期的卡片（陷阱卡和增益卡裝備超過 N 天）
        public int RemoveExpiredCards(int daysOld = 7)
        {
            var expiredDate = DateTime.Now.AddDays(-daysOld);

            // 查詢過期的陷阱卡和增益卡（Type 1 和 2）
            var expiredCards = _dbContext.UserCard
                .Where(x => x.DeletedAt == null
                    && x.IsEquipped == true
                    && x.EquippedAt != null
                    && x.EquippedAt < expiredDate
                    && (x.Card.Type == CARD_TYPE.TRAP || x.Card.Type == CARD_TYPE.BUFF))
                .ToList();

            foreach (var userCard in expiredCards)
            {
                // Soft delete 過期的卡片
                userCard.DeletedAt = DateTime.Now;
                userCard.IsEquipped = false;
            }

            _dbContext.SaveChanges();

            return expiredCards.Count;
        }
    }
}
