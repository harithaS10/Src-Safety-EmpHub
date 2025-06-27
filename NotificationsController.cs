using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using SafetyToolBoxAPI.Common;
using SafetyToolBoxDL.CommonDB;
using SafetyToolBoxDL.CustomDB;
using SafetyToolBoxDL.HRMSDB;
using SafetyToolBoxDL.SafetyDB;

namespace SafetyToolBoxAPI.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly VivifyCommonContext CommonDBContext;
        private readonly VivifySafetyToolboxContext SafetyDBContext;
        private readonly CustomDBContext CustDBContext;
        private readonly VivifyHrmsContext HRMSDBContext;
        private IConfiguration Config;
        private readonly HttpClient _httpClient;
        public NotificationsController(VivifyCommonContext context, VivifySafetyToolboxContext SafteyContxt, IConfiguration _config, CustomDBContext _custObject, VivifyHrmsContext hrmsContext)
        {
            CommonDBContext = context;
            SafetyDBContext = SafteyContxt;
            CustDBContext = _custObject;
            HRMSDBContext = hrmsContext;
            Config = _config;
            _httpClient = new HttpClient();
        }

        [HttpPost]
        public VivifyResponse<bool> PushToken([FromBody] NotificationToken token)
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                //var EmpNo = "1001";
                var notificationToken = CommonDBContext.NotificationTokens.Where(x => x.Token == token.Token).FirstOrDefault();
                if (notificationToken == null)
                {
                    token.EmpNo = Empno.ToString();
                    token.CreatedDate = DateTime.UtcNow;
                    CommonDBContext.Entry(token).State = EntityState.Added;
                    CommonDBContext.SaveChanges();
                    CommonDBContext.SaveChanges();
                }

                CommonDBContext.SaveChanges();
                return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Token Registered Successfully", Result = true };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<bool> { StatusCode = 400, StatusDesc = ex.Message + ":" + ex.StackTrace, Result = false };
            }
        }
        [HttpPost]
        public async Task<IActionResult> SendNotification([FromBody] NotificationRequest request)
        {
            var tokens = await CommonDBContext.NotificationTokens.Select(t => t.Token).ToListAsync();
            if (tokens.Count == 0) return BadRequest("No registered users found.");

            var expoPushMessages = new List<object>();
            foreach (var token in tokens)
            {
                expoPushMessages.Add(new
                {
                    to = token,
                    title = request.Title,
                    body = request.Message,
                    sound = "default"
                });
            }

            var jsonPayload = JsonConvert.SerializeObject(expoPushMessages);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://exp.host/--/api/v2/push/send", content);
            var responseString = await response.Content.ReadAsStringAsync();

            return Ok(new { message = "Notifications sent successfully.", response = responseString });
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> SendDailyTrainingReminders()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var punchedInEmpNos = await HRMSDBContext.EmpBiometrics
                .Where(p => p.BusinessDate == today)
                .Select(p => p.EmpNo)
                .Distinct()
                .ToListAsync();

            if (!punchedInEmpNos.Any())
                return Ok("No punch-ins found today.");

            var activeDailyTrainings = await SafetyDBContext.TrainingInfos
                .Where(t => t.IsDailyVideo == true && t.IsActive == true)
                .Select(t => t.TrainingId)
                .ToListAsync();

            var notificationsSent = 0;

            foreach (var empNo in punchedInEmpNos)
            {

                var todayDateTime = today.ToDateTime(TimeOnly.MinValue);

                bool hasCompleted = await SafetyDBContext.EmpTrainStatuses
                    .AnyAsync(e =>
                        e.EmpNo == empNo &&
                        activeDailyTrainings.Contains(e.TrainingId) &&
                        e.IsDailyVideo == true &&
                        e.CompletedOn.HasValue &&
                        e.CompletedOn.Value.Date == todayDateTime.Date
                    );

                if (!hasCompleted)
                {
                    var token = await CommonDBContext.NotificationTokens
                        .Where(x => x.EmpNo == empNo.ToString())
                        .Select(x => x.Token)
                        .FirstOrDefaultAsync();

                    if (!string.IsNullOrEmpty(token))
                    {
                        var message = new[]
                        {
                    new
                    {
                        to = token,
                        title = "Reminder 🎥",
                        body = "You have not completed today's safety video. Please finish it before logout.",
                        sound = "default",
                        data = new { screen = "DailyVideos" }
                    }
                };

                        var jsonPayload = JsonConvert.SerializeObject(message);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync("https://exp.host/--/api/v2/push/send", content);
                        notificationsSent++;
                    }
                }
            }

            return Ok($"Notifications sent to {notificationsSent} employees.");
        }


        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> SendDailyNotification()
        {
            var vwDailyVideos = await SafetyDBContext.VwDailyVideos.ToListAsync();
            if (vwDailyVideos.Count == 0) return BadRequest("No registered users found.");

            var expoPushMessages = new List<object>();
            foreach (var dailyVideo in vwDailyVideos)
            {
                expoPushMessages.Add(new
                {
                    to = dailyVideo.Token,
                    title = "Reminder ⏰",
                    body = $"Hi {dailyVideo.FirstName}, You have {dailyVideo.NoofTraining} pending 🎥 videos to complete before logging out.",
                    sound = "default",
                    data = new
                    {
                        screen = "DailyVideos"
                    }
                });
            }

            var jsonPayload = JsonConvert.SerializeObject(expoPushMessages);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://exp.host/--/api/v2/push/send", content);
            var responseString = await response.Content.ReadAsStringAsync();

            return Ok(new { message = "Notifications sent successfully.", response = responseString });
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> NotifyEveryTwoHoursUntilVideoComplete()
        {
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);

            var punchIns = await HRMSDBContext.EmpBiometrics
                .Where(p => p.BusinessDate == today && p.CheckInType == 1 && p.CheckInTime != null)
                .ToListAsync();

            var notifiedEmployees = new List<object>();

            foreach (var punch in punchIns)
            {
                if (punch.CheckInTime == null)
                    continue;

                var empNo = punch.EmpNo;
                var punchInTime = punch.CheckInTime.Value;
                var minutesSincePunch = (now - punchInTime).TotalMinutes;

                // 🔁 Only continue if it's 2h, 4h, 6h... since punch-in
                if (minutesSincePunch < 120 || ((int)minutesSincePunch % 120) > 10)
                    continue;

                // ✅ Has employee completed video?
                var hasCompleted = await SafetyDBContext.EmpTrainStatuses
                    .AnyAsync(t =>
                        t.EmpNo == empNo &&
                        t.IsDailyVideo == true &&
                        t.CompletedOn.HasValue &&
                        t.CompletedOn.Value.Date == now.Date
                    );

                if (hasCompleted)
                    continue;

                // ✅ Get employee name
                var empName = await CommonDBContext.EmployeeInfos
                    .Where(e => e.EmpNo == empNo)
                    .Select(e => e.FirstName)
                    .FirstOrDefaultAsync();

                var name = string.IsNullOrWhiteSpace(empName) ? "Employee" : empName;

                // ✅ Get push token
                var token = await CommonDBContext.NotificationTokens
                    .Where(x => x.EmpNo == empNo.ToString())
                    .OrderByDescending(x => x.CreatedDate)
                    .Select(x => x.Token)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrEmpty(token))
                    continue;

                // ✅ Send notification
                var message = new[]
                {
            new
            {
                to = token,
                title = "Reminder 🎥",
                body = $"Hi {name}, your safety video is still pending. Please complete it now.",
                sound = "default",
                data = new { screen = "DailyVideos" }
            }
        };

                var jsonPayload = JsonConvert.SerializeObject(message);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                await _httpClient.PostAsync("https://exp.host/--/api/v2/push/send", content);

                notifiedEmployees.Add(new
                {
                    EmpNo = empNo,
                    Name = name,
                    PunchInTime = punchInTime,
                    SentAt = now
                });
            }

            return Ok(new
            {
                message = $"Notifications sent to {notifiedEmployees.Count} employees (every 2 hours after punch-in until video complete).",
                notified = notifiedEmployees
            });
        }
        [HttpGet]
        [AllowAnonymous]
        [ActionName("SendNearMissReportNotifications")]
        public async Task<IActionResult> SendNearMissReportNotifications()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var notificationsSent = new List<object>();

            // 🔍 Step 1: Get all tokens
            var allTokens = await CommonDBContext.NotificationTokens
                .Where(t => !string.IsNullOrEmpty(t.Token))
                .ToListAsync();

            var latestTokens = allTokens
                .GroupBy(t => Convert.ToInt64(t.EmpNo))
                .Select(g => g.OrderByDescending(t => t.CreatedDate).First())
                .ToDictionary(t => Convert.ToInt64(t.EmpNo), t => t.Token);

            // ✅ First Condition: New Near Miss Created Today
            var newReports = await SafetyDBContext.NearMissReports
                .Where(r => r.CrtDate >= today && r.CrtDate < tomorrow)
                .ToListAsync();

            if (newReports.Any())
            {
                foreach (var kv in latestTokens)
                {
                    var token = kv.Value;

                    var message = new[]
                    {
                new {
                    to = token,
                    title = "Near Miss ⚠️",
                    body = "A near miss has been reported today. Please stay safe and alert!",
                    sound = "default",
                    data = new { screen = "NearMiss" }
                }
            };

                    var jsonPayload = JsonConvert.SerializeObject(message);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync("https://exp.host/--/api/v2/push/send", content);

                    notificationsSent.Add(new { Type = "New Report", Token = token, EmpNo = kv.Key });
                }
            }

            // ✅ Second Condition: Action Taken Today
            var updatedReports = await SafetyDBContext.NearMissReports
                .Where(r => !string.IsNullOrEmpty(r.ActionTaken)
                            && r.UpdDate.HasValue
                            && r.UpdDate.Value >= today && r.UpdDate.Value < tomorrow)
                .ToListAsync();

            if (updatedReports.Any())
            {
                foreach (var kv in latestTokens)
                {
                    var token = kv.Value;

                    var message = new[]
                    {
                new {
                    to = token,
                    title = "Near Miss - Action Taken ✔️",
                    body = "Corrective action has been taken for a near miss. Please stay safe and alert!",
                    sound = "default",
                    data = new { screen = "NearMiss" }
                }
            };

                    var jsonPayload = JsonConvert.SerializeObject(message);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync("https://exp.host/--/api/v2/push/send", content);

                    notificationsSent.Add(new { Type = "Action Taken", Token = token, EmpNo = kv.Key });
                }
            }

            if (!notificationsSent.Any())
            {
                return Ok(new { message = "No new or updated near miss reports today." });
            }

            return Ok(new
            {
                message = $"Sent {notificationsSent.Count} notifications based on today's activity.",
                notified = notificationsSent
            });
        }



    }
}
