using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SafetyToolBoxDL.CommonDB;
using SafetyToolBoxDL.CustomDB;
using SafetyToolBoxDL.HRMSDB;
using SafetyToolBoxDL.SafetyDB;
using System.Net.Http;
using System.Text;

namespace SafetyToolBoxAPI.Notifications
{
    public class TrainingNotifications
    {
        private VivifySafetyToolboxContext safetyDBContext;
        private TrainingInfo objTrainingInfo;

        public TrainingNotifications(VivifySafetyToolboxContext safetyDBContext, TrainingInfo objTrainingInfo)
        {
            this.safetyDBContext = safetyDBContext;
            this.objTrainingInfo = objTrainingInfo;
        }

        public async Task SendNotification()
        {
            var vwDailyVideos = await safetyDBContext.VwTrainingVideos.Where(c => c.TrainingId== objTrainingInfo.TrainingId).ToListAsync();
            if (vwDailyVideos.Count == 0) return;
           var _httpClient = new HttpClient();
            var expoPushMessages = new List<object>();
            foreach (var dailyVideo in vwDailyVideos)
            {
                expoPushMessages.Add(new
                {
                    to = dailyVideo.Token,
                    title = "New Training 📚",
                    body = $" We have assigned a new training  {dailyVideo.TrainingName}  for you. Please complete before duedate",
                    sound = "default",
                    data = new
                    {
                        screen = "VideoScreen"
                    }
                });
            }

            var jsonPayload = JsonConvert.SerializeObject(expoPushMessages);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://exp.host/--/api/v2/push/send", content);
        }
    }
}
