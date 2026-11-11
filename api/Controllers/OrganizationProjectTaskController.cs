using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Homo.Bet.Api
{

    [AuthorizeFactory]
    [BelongOrgFactory]
    [Route("v1/organizations/{organizationId}/projects/{projectId}/tasks")]
    public class TaskController : ControllerBase
    {
        private readonly BargainingChipDBContext _dbContext;
        private readonly string _githubToken;
        private readonly Dictionary<string, int> _defaultCoinMapping = new Dictionary<string, int>() { { "planning", 4 }, { "plan", 4 }, { "r&d", 10 }, { "rd", 10 }, { "develop", 5 }, { "marketing", 3 }, { "operation", 3 }, { "bd", 10 }, { "bug", 2 } };
        public TaskController(BargainingChipDBContext dbContext, IOptions<AppSettings> options)
        {
            _dbContext = dbContext;
            _githubToken = options.Value.Secrets.GitHubToken;
        }

        [HttpGet]
        public ActionResult<dynamic> getList([FromRoute] long organizationId, [FromRoute] long projectId, [FromQuery] string name, [FromQuery] int limit, [FromQuery] int page, Homo.Bet.Api.DTOs.JwtExtraPayload extraPayload)
        {
            long ownerId = extraPayload.Id;
            List<Task> records = TaskDataservice.GetList(_dbContext, organizationId, projectId, page, limit, name);
            return new
            {
                tasks = records,
                rowNums = TaskDataservice.GetRowNum(_dbContext, organizationId, projectId, name)
            };
        }

        [HttpGet]
        [Route("all")]
        public ActionResult<dynamic> getAll([FromRoute] long organizationId, [FromRoute] long projectId, [FromQuery] string name, [FromQuery] int limit, [FromQuery] int page, [FromQuery] string externalIds, Homo.Bet.Api.DTOs.JwtExtraPayload extraPayload)
        {
            long ownerId = extraPayload.Id;
            List<string> listOfExternalId = externalIds.Split(",").ToList<string>();
            return TaskDataservice.GetAll(_dbContext, organizationId, projectId, null, null, listOfExternalId);
        }

        [HttpPost]
        [Route("get-list")]
        public async Task<dynamic> GetList([FromRoute] long organizationId, [FromRoute] long projectId, [FromBody] List<string> extIds, Homo.Bet.Api.DTOs.JwtExtraPayload extraPayload)
        {
            var tasks = TaskDataservice.GetListByExternalIds(_dbContext, projectId, extIds);
            var taskExtIds = tasks.Select(task => task.ExternalId);

            var allTask = new List<Task>();
            allTask.AddRange(tasks);

            var githubIssues = new List<GithubIssueIdentify>();
            // get github issue project and status and relation of project and issues;
            using (HttpClient githubClient = new HttpClient())
            {
                githubClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
                githubClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _githubToken);
                string url = $"https://api.github.com/graphql";
                var httpContent = new StringContent(@$"{{""query"":""{{ organization(login: \""homo-tw\"") {{ repository(name: \""itemhub\"") {{ issues(first: 40, states: OPEN, orderBy: {{field: CREATED_AT, direction: DESC}}) {{ nodes {{ number, id, title, closed, projectItems(first: 20) {{ edges {{ node {{id}} }},nodes {{ fieldValueByName(name: \""Status\"") {{ ... on ProjectV2ItemFieldSingleSelectValue {{name, optionId}} }} }} }}, projectsV2(first:20) {{ nodes{{ id, title, field(name: \""Status\"") {{ ... on ProjectV2SingleSelectField {{ name, id }} }} }} }} }} }} }} }} }}""}}", System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage response = await githubClient.PostAsync(url, httpContent);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                JObject graphqlResponse = JObject.Parse(jsonResponse);

                githubIssues = graphqlResponse["data"]["organization"]["repository"]["issues"]["nodes"].ToList<dynamic>().Where(item => (bool)item.closed != true).Select(item =>
                    {
                        var projects = ((JArray)item["projectsV2"]["nodes"]).ToList<dynamic>();
                        var projectItems = ((JArray)item["projectItems"]["nodes"]).ToList<dynamic>().Where(item => item["fieldValueByName"] != null).ToList();
                        var projectEdges = ((JArray)item["projectItems"]["edges"]).ToList<dynamic>().Where(item => item["node"] != null).ToList();
                        return new GithubIssueIdentify
                        {
                            Number = (string)item["number"],
                            IssueId = item["id"],
                            ProjectId = projects.Count > 0 ? projects[0]["id"] : null,
                            FieldId = projects.Count > 0 ? projects[0]["field"]["id"] : null,
                            ConnectionId = projectEdges.Count > 0 ? projectEdges[0]["node"]["id"] : null,
                            OptionId = projectItems.Count > 0 ? projectItems[0]["fieldValueByName"]["optionId"] : null,
                            Title = (string)item["title"]
                        };
                    }).ToList();
            }

            var betLogs = CoinsLogDataService.GetAllByTaskIds(_dbContext, allTask.Select(item => { long? result; result = item.Id; return result; }).ToList(), COIN_LOG_TYPE.BET);
            return allTask.Select(task =>
            {
                var totalQty = System.Math.Abs(betLogs.Where(item => item.TaskId == task.Id).Sum(item => item.Qty));
                var ownerFreeBet = System.Math.Abs(betLogs.Where(item => item.TaskId == task.Id && item.OwnerId == extraPayload.Id && item.IsLock == false).Sum(item => item.Qty));
                var ownerLockedBet = System.Math.Abs(betLogs.Where(item => item.TaskId == task.Id && item.OwnerId == extraPayload.Id && item.IsLock == true).Sum(item => item.Qty));
                var log = betLogs.Where(item => item.OwnerId == extraPayload.Id && item.IsLock != true && item.TaskId == task.Id).FirstOrDefault();
                var matchedIssue = githubIssues.Where(item => item.Number == task.ExternalId).FirstOrDefault();

                return new
                {
                    task.Id,
                    excludeOwnerBet = totalQty - ownerFreeBet - ownerLockedBet,
                    ownerLockedBet,
                    ownerFreeBet,
                    assigneeId = task.AssigneeId,
                    assignee = task.Assignee,
                    expectedFinishAt = task.ExpectedFinishAt,
                    currentCoinLogId = log == null ? 0 : log.Id,
                    status = task.Status,
                    externalId = task.ExternalId,
                    githubStatusFieldId = matchedIssue?.FieldId,
                    githubProjectId = matchedIssue?.ProjectId,
                    githubIssueId = matchedIssue?.IssueId,
                    githubOptionId = matchedIssue?.OptionId,
                    githubConnectionId = matchedIssue.ConnectionId,
                };
            }).ToList();
        }

        [HttpPost]
        [Route("{id}/update-current-coin-log")]
        public ActionResult<dynamic> updateCurrentCoinLogs([FromRoute] long organizationId, [FromRoute] long projectId, [FromRoute] long id, Homo.Bet.Api.DTOs.JwtExtraPayload extraPayload, [FromBody] DTOs.CoinLog dto)
        {
            var task = TaskDataservice.GetOne(_dbContext, organizationId, projectId, id);
            if (task == null)
            {
                throw new Homo.Core.Constants.CustomException(ERROR_CODE.DATA_NOT_FOUND, System.Net.HttpStatusCode.NotFound);
            }
            // first one bet give author of issue one coin
            int taskBetCoins = CoinsLogDataService.GetTaskBetCoins(_dbContext, task.Id);
            List<CoinLog> logs = CoinsLogDataService.GetAll(_dbContext, task.Id, task.CreatedBy, COIN_LOG_TYPE.EARN);
            if (taskBetCoins == 0 && logs.Count == 0)
            {
                CoinsLogDataService.Create(_dbContext, task.CreatedBy, task.Id, extraPayload.Id, COIN_LOG_TYPE.EARN, new DTOs.CoinLog() { Qty = 1 });
            }

            CoinLog log = CoinsLogDataService.GetFreeOneByTaskIdAndOwnerId(_dbContext, task.Id, extraPayload.Id, false);
            if (log == null)
            {
                log = CoinsLogDataService.Create(_dbContext, extraPayload.Id, task.Id, extraPayload.Id, COIN_LOG_TYPE.BET, dto);
            }
            else
            {
                if (task.AssigneeId != null && System.Math.Abs(dto.Qty) < System.Math.Abs(log.Qty))
                {
                    throw new Homo.Core.Constants.CustomException(ERROR_CODE.TASK_HAS_CLAIMED, System.Net.HttpStatusCode.Forbidden);
                }
                CoinsLogDataService.Update(_dbContext, log, extraPayload.Id, dto);
            }
            return new { status = Homo.Core.Constants.CUSTOM_RESPONSE.OK };
        }

        [HttpPost]
        [Route("{id}/assign")]
        public ActionResult<dynamic> assign([FromRoute] long organizationId, [FromRoute] long projectId, [FromRoute] long id, Homo.Bet.Api.DTOs.JwtExtraPayload extraPayload, [FromBody] DTOs.TaskWorkDays dto)
        {
            var task = TaskDataservice.GetOne(_dbContext, organizationId, projectId, id);
            if (task == null)
            {
                throw new Homo.Core.Constants.CustomException(ERROR_CODE.DATA_NOT_FOUND, System.Net.HttpStatusCode.NotFound);
            }

            if (task.AssigneeId != null)
            {
                throw new Homo.Core.Constants.CustomException(ERROR_CODE.TASK_HAS_CLAIMED, System.Net.HttpStatusCode.NotFound);
            }
            TaskDataservice.Assign(_dbContext, task, extraPayload.Id, dto.WorkDays);
            if (extraPayload.Id == 4 || extraPayload.Id == 5)
            {
                var username = extraPayload.Id == 4 ? "miterfrants" : "vickychou99";
                using (HttpClient githubClient = new HttpClient())
                {
                    githubClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
                    githubClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _githubToken);
                    var httpContent = new StringContent($@"{{""assignees"":[""{username}""]}}", System.Text.Encoding.UTF8, "application/json");
                    var url = $"https://api.github.com/repos/homo-tw/itemhub/issues/{task.ExternalId}/assignees";
                    var response = githubClient.PostAsync(url, httpContent);
                }
            }

            return new { status = Homo.Core.Constants.CUSTOM_RESPONSE.OK };
        }

        [HttpPost]
        [Route("{id}/mark-finish")]
        public ActionResult<dynamic> markFinish([FromRoute] long organizationId, [FromRoute] long projectId, [FromRoute] long id, Homo.Bet.Api.DTOs.JwtExtraPayload extraPayload)
        {
            var task = TaskDataservice.GetOne(_dbContext, organizationId, projectId, id);
            if (task == null)
            {
                throw new Homo.Core.Constants.CustomException(ERROR_CODE.DATA_NOT_FOUND, System.Net.HttpStatusCode.NotFound);
            }

            if (task.AssigneeId != extraPayload.Id)
            {
                throw new Homo.Core.Constants.CustomException(ERROR_CODE.TASK_HAS_CLAIMED, System.Net.HttpStatusCode.NotFound);
            }
            TaskDataservice.MarkFinish(_dbContext, task);
            if (extraPayload.Id == 4 || extraPayload.Id == 5)
            {
                var username = extraPayload.Id == 4 ? "miterfrants" : "vickychou99";
                using (HttpClient githubClient = new HttpClient())
                {
                    githubClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
                    githubClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _githubToken);
                    var httpContent = new StringContent($@"{{""assignees"":[""{username}""]}}", System.Text.Encoding.UTF8, "application/json");
                    var url = $"https://api.github.com/repos/homo-tw/itemhub/issues/{task.ExternalId}/assignees";
                    var response = githubClient.PostAsync(url, httpContent);
                }
            }
            return new { status = Homo.Core.Constants.CUSTOM_RESPONSE.OK };
        }

        [HttpPost]
        [Route("{id}/done")]
        public ActionResult<dynamic> done([FromRoute] long organizationId, [FromRoute] long projectId, [FromRoute] long id, Homo.Bet.Api.DTOs.JwtExtraPayload extraPayload)
        {
            var task = TaskDataservice.GetOne(_dbContext, organizationId, projectId, id);
            if (task == null)
            {
                throw new Homo.Core.Constants.CustomException(ERROR_CODE.DATA_NOT_FOUND, System.Net.HttpStatusCode.NotFound);
            }
            if (task.Status != TASK_STATUS.BE_MARK_FINSIH)
            {
                throw new Homo.Core.Constants.CustomException(ERROR_CODE.TASK_STATUS_ERROR, System.Net.HttpStatusCode.NotFound);
            }

            if (task.AssigneeId == extraPayload.Id)
            {
                throw new Homo.Core.Constants.CustomException(ERROR_CODE.ASSIGNEE_NOT_ALLOW_APPROVE, System.Net.HttpStatusCode.NotFound);
            }
            TaskDataservice.Done(_dbContext, extraPayload.Id, task);
            int coins = CoinsLogDataService.GetTaskBetCoins(_dbContext, task.Id);
            long assigneeId = task.AssigneeId.GetValueOrDefault();
            int assigneeMultiplier = CardEffectDataservice.GetEarnMultiplier(_dbContext, assigneeId);
            CoinsLogDataService.Create(_dbContext, assigneeId, task.Id, extraPayload.Id, COIN_LOG_TYPE.EARN, new DTOs.CoinLog() { Qty = -coins * assigneeMultiplier });

            int bonus = coins == 0 ? 0 : (int)System.Math.Ceiling(((decimal)System.Math.Abs(coins) / (decimal)5));
            if (bonus <= 0)
            {
                bonus = 1;
            }
            int reviewerMultiplier = CardEffectDataservice.GetEarnMultiplier(_dbContext, extraPayload.Id);
            CoinsLogDataService.Create(_dbContext, extraPayload.Id, task.Id, extraPayload.Id, COIN_LOG_TYPE.EARN, new DTOs.CoinLog() { Qty = bonus * reviewerMultiplier });
            return new { status = Homo.Core.Constants.CUSTOM_RESPONSE.OK };
        }
    }


    public class GithubIssueIdentify
    {
        public string Number { get; set; }
        public string ProjectId { get; set; }
        public string IssueId { get; set; }
        public string FieldId { get; set; }
        public string OptionId { get; set; }
        public string ConnectionId { get; set; }
        public string Title { get; set; }
        public List<GithubProjectStatusOption> Options { get; set; }
    }

    public class GithubProjectStatusOption
    {
        public string Name { get; set; }
        public string Id { get; set; }
    }
}
