﻿using AspHook.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace AspHook.Controllers
{
    public class WebhooksController : ApiController
    {
        string appSecret = ConfigurationManager.AppSettings["messager::appSecret"];
        string pageToken = ConfigurationManager.AppSettings["messager::pageToken"];
        string verifyToken = ConfigurationManager.AppSettings["messager::verifyToken"];
        Logger _logger;

        public WebhooksController()
        {
            _logger = LogManager.GetLogger("fileLogger");
        }

        public HttpResponseMessage Get()
        {
            _logger.Info("Get Method Started");
            var querystrings = Request.GetQueryNameValuePairs().ToDictionary(x => x.Key, x => x.Value);
            _logger.Info(JsonConvert.SerializeObject(querystrings));
            if (querystrings["hub.verify_token"] == verifyToken)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(querystrings["hub.challenge"], Encoding.UTF8, "text/plain")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        [HttpPost]
        public async Task<HttpResponseMessage> Post()
        {
            _logger.Info("Post Method Started");
            var signature = Request.Headers.GetValues("X-Hub-Signature").FirstOrDefault().Replace("sha1=", "");
            var body = await Request.Content.ReadAsStringAsync();
            _logger.Info(body);
            if (!VerifySignature(signature, body))
                return new HttpResponseMessage(HttpStatusCode.BadRequest);

            var value = JsonConvert.DeserializeObject<WebhookModel>(body);
            if (value._object != "page")
                return new HttpResponseMessage(HttpStatusCode.OK);

            foreach (var item in value.entry[0].messaging)
            {
                if (item.message == null && item.postback == null)
                    continue;
                else
                    await SendMessage(GetMessageTemplate(item.message.text, item.sender.id));
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private bool VerifySignature(string signature, string body)
        {
            var hashString = new StringBuilder();
            using (var crypto = new HMACSHA1(Encoding.UTF8.GetBytes(appSecret)))
            {
                var hash = crypto.ComputeHash(Encoding.UTF8.GetBytes(body));
                foreach (var item in hash)
                    hashString.Append(item.ToString("X2"));
            }

            return hashString.ToString().ToLower() == signature.ToLower();
        }

        /// <summary>
        /// get text message template
        /// </summary>
        /// <param name="text">text</param>
        /// <param name="sender">sender id</param>
        /// <returns>json</returns>
        private JObject GetMessageTemplate(string text, string sender)
        {
            return JObject.FromObject(new
            {
                recipient = new { id = sender },
                message = new { text = text }
            });
        }

        /// <summary>
        /// send message
        /// </summary>
        /// <param name="json">json</param>
        private async Task SendMessage(JObject json)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage res = await client.PostAsync($"https://graph.facebook.com/v2.6/me/messages?access_token={pageToken}", new StringContent(json.ToString(), Encoding.UTF8, "application/json"));
            }
        }
    }
}
