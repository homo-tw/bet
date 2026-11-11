using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;

namespace Homo.Bet.Api
{
    [Route("v1/cards")]
    [AuthorizeFactory]
    public class CardsController : ControllerBase
    {
        private readonly CardRepository _cardRepository;
        private readonly BargainingChipDBContext _dbContext;

        public CardsController(CardRepository cardRepository, BargainingChipDBContext dbContext)
        {
            _cardRepository = cardRepository;
            _dbContext = dbContext;
        }

        // GET /v1/cards - 取得所有可用的卡片（商店卡片）
        [HttpGet]
        public ActionResult<dynamic> GetAvailableCards()
        {
            var cards = _cardRepository.GetAvailableCards();
            var cardsDto = cards.Select(card => new
            {
                id = card.Id,
                name = card.Name,
                type = card.Type.ToString(),
                description = card.Description,
                cost = card.Cost
            }).ToList();

            return new { cards = cardsDto };
        }

        // GET /v1/cards/my-cards - 取得使用者擁有的卡片
        [HttpGet]
        [Route("my-cards")]
        public ActionResult<dynamic> GetMyCards(DTOs.JwtExtraPayload extraPayload)
        {
            var userCards = _cardRepository.GetUserCards(extraPayload.Id);
            var userCardsDto = userCards.Select(userCard => new
            {
                id = userCard.Id,
                cardId = userCard.CardId,
                name = userCard.Card.Name,
                type = userCard.Card.Type.ToString(),
                description = userCard.Card.Description,
                cost = userCard.Card.Cost,
                isEquipped = userCard.IsEquipped,
                purchaseDate = userCard.CreatedAt,
                equippedAt = userCard.EquippedAt
            }).ToList();

            return new { cards = userCardsDto };
        }

        // GET /v1/cards/equipped - 取得已裝備的卡片
        [HttpGet]
        [Route("equipped")]
        public ActionResult<dynamic> GetEquippedCards(DTOs.JwtExtraPayload extraPayload)
        {
            var equippedCards = _cardRepository.GetEquippedCards(extraPayload.Id);
            var equippedCardsDto = equippedCards.Select(userCard => new
            {
                id = userCard.Id,
                cardId = userCard.CardId,
                name = userCard.Card.Name,
                type = userCard.Card.Type.ToString(),
                description = userCard.Card.Description,
                equippedAt = userCard.EquippedAt
            }).ToList();

            return new { cards = equippedCardsDto };
        }

        // POST /v1/cards/buy - 購買卡片
        [HttpPost]
        [Route("buy")]
        public ActionResult<dynamic> BuyCard([FromBody] DTOs.BuyCard dto, DTOs.JwtExtraPayload extraPayload)
        {
            // 檢查卡片是否存在
            var card = _cardRepository.GetCardById(dto.CardId);
            if (card == null)
            {
                return BadRequest(new { message = "卡片不存在" });
            }

            // 檢查使用者是否有足夠的 coins
            var earnCoins = CoinsLogDataService.GetEarnCoins(_dbContext, extraPayload.Id);
            if (earnCoins < card.Cost)
            {
                return BadRequest(new { message = "存款不足" });
            }

            // 扣除 coins（使用 BUY 類型）
            var coinLogDto = new DTOs.CoinLog { Qty = card.Cost };
            CoinsLogDataService.Create(_dbContext, extraPayload.Id, null, extraPayload.Id, COIN_LOG_TYPE.BUY, coinLogDto);

            // 購買卡片
            var userCard = _cardRepository.BuyCard(extraPayload.Id, dto.CardId);

            return new
            {
                message = "購買成功",
                userCard = new
                {
                    id = userCard.Id,
                    cardId = userCard.CardId,
                    purchaseDate = userCard.CreatedAt
                }
            };
        }

        // POST /v1/cards/equip - 裝備卡片
        [HttpPost]
        [Route("equip")]
        public ActionResult<dynamic> EquipCard([FromBody] DTOs.EquipCard dto, DTOs.JwtExtraPayload extraPayload)
        {
            var userCard = _cardRepository.EquipCard(extraPayload.Id, dto.UserCardId, dto.TriggerCondition);

            if (userCard == null)
            {
                // 需要更詳細的錯誤訊息，但先用通用訊息
                return BadRequest(new { message = "裝備失敗，可能原因：已達上限（最多3張）、卡片不存在、陷阱卡只能在週一裝備、或陷阱卡未填寫觸發條件" });
            }

            return new
            {
                message = "裝備成功",
                userCard = new
                {
                    id = userCard.Id,
                    cardId = userCard.CardId,
                    isEquipped = userCard.IsEquipped,
                    equippedAt = userCard.EquippedAt,
                    triggerCondition = userCard.TriggerCondition
                }
            };
        }

        // POST /v1/cards/unequip - 卸下卡片
        [HttpPost]
        [Route("unequip")]
        public ActionResult<dynamic> UnequipCard([FromBody] DTOs.EquipCard dto, DTOs.JwtExtraPayload extraPayload)
        {
            var userCard = _cardRepository.UnequipCard(extraPayload.Id, dto.UserCardId);

            if (userCard == null)
            {
                return BadRequest(new { message = "卸下失敗，卡片不存在" });
            }

            return new
            {
                message = "卸下成功",
                userCard = new
                {
                    id = userCard.Id,
                    cardId = userCard.CardId,
                    isEquipped = userCard.IsEquipped
                }
            };
        }

        // POST /v1/cards/use - 使用卡片（僅限魔法卡）
        [HttpPost]
        [Route("use")]
        public ActionResult<dynamic> UseCard([FromBody] DTOs.EquipCard dto, DTOs.JwtExtraPayload extraPayload)
        {
            var userCard = _cardRepository.UseCard(extraPayload.Id, dto.UserCardId, out var effect);

            if (userCard == null)
            {
                return BadRequest(new { message = "使用失敗，卡片不存在或不是魔法卡" });
            }

            return new
            {
                message = "使用成功",
                cardName = userCard.Card.Name,
                effect,
                userCard = new
                {
                    id = userCard.Id,
                    cardId = userCard.CardId
                }
            };
        }
    }
}
