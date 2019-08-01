﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpiderX.Extensions.Http;
using SpiderX.Http;
using SpiderX.Http.Util;
using SpiderX.Proxy;
using SpiderX.Tools;

namespace SpiderX.Business.LaGou
{
	public sealed partial class LaGouBll
	{
		private class PcWebCollector : CollectorBase
		{
			private const int MaxPage = 2;

			public bool UseProxy { get; set; }

			public override async Task<LaGouResponseDataCollection> CollectAsync(LaGouSearchParam searchParam)
			{
				string encodedCityName = WebTool.UrlEncodeByW3C(searchParam.City);
				string encodedKeyword = WebTool.UrlEncodeByW3C(searchParam.Keyword);
				LaGouResponseDataCollection dataCollection = new LaGouResponseDataCollection();
				using (var cookieClient = CreateCookiesWebClient())
				{
					Uri jobsListPageUri = PcWebApiProvider.GetJobListUri(encodedCityName, encodedKeyword);
					cookieClient.DefaultRequestHeaders.Referrer = jobsListPageUri;
					//Init Cookies
					ResetHttpClientCookies(cookieClient, jobsListPageUri).ConfigureAwait(false).GetAwaiter().GetResult();
					await Task.Delay(3333);
					Uri referer = PcWebApiProvider.GetPostionAjaxReferer(encodedCityName, encodedKeyword);
					using (var positionAjaxClient = CreatePositionAjaxWebClient(referer))
					{
						//Preparing
						positionAjaxClient.DefaultRequestHeaders.Referrer = jobsListPageUri;
						Uri positionAjaxUri = PcWebApiProvider.GetPositionAjaxUri(encodedCityName);
						var tasks = new Task[MaxPage];
						//Start tasks
						for (int i = 1; i <= MaxPage; i++)
						{
							HttpContent httpContent = PcWebApiProvider.GetPositionAjaxFormData(encodedKeyword, i.ToString());
							tasks[i - 1] = GetResponseData(positionAjaxClient, positionAjaxUri, jobsListPageUri, httpContent, cookieClient.CookieContainer, dataCollection);
							Thread.Sleep(RandomTool.NextIntSafely(5000, 10000));
						}
						//Wait all tasks
						try
						{
							Task.WaitAll(tasks);
						}
						catch
						{
							throw;
						}
					}
				}
				foreach (var pos in dataCollection.Positions)
				{
					pos.Value.Keyword = encodedKeyword;
				}
				foreach (var com in dataCollection.Companies)
				{
					com.Value.CityName = encodedCityName;
				}
				return dataCollection;
			}

			private SpiderHttpClient CreatePositionAjaxWebClient(Uri referer)
			{
				SocketsHttpHandler handler = new SocketsHttpHandler()
				{
					AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
					UseCookies = true,
					UseProxy = UseProxy
				};
				if (UseProxy)
				{
					handler.Proxy = HttpConsole.LocalWebProxy;
				}
				SpiderHttpClient client = new SpiderHttpClient(handler);
				var headers = client.DefaultRequestHeaders;
				headers.Host = PcWebApiProvider.HomePageUri.Host;
				headers.Referrer = referer;
				headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
				headers.Add("Accept-Encoding", "br");
				headers.Add("Accept-Language", "zh-CN,zh;q=0.9");
				headers.Add("X-Requested-With", "XMLHttpRequest");
				headers.Add("X-Anit-Forge-Code", "0");
				headers.Add("X-Anit-Forge-Token", "None");
				headers.Add("Origin", PcWebApiProvider.HomePageUri.AbsoluteUri);
				headers.Add("User-Agent", HttpConsole.DefaultPcUserAgent);
				return client;
			}

			private SpiderHttpClient CreateCookiesWebClient()
			{
				SocketsHttpHandler httpHandler = new SocketsHttpHandler()
				{
					AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
					UseCookies = true,
					UseProxy = UseProxy
				};
				if (UseProxy)
				{
					//var uris = GetUrisFromDb();
					//var webProxy = CreateWebProxy(uris);
					//httpHandler.Proxy = webProxy;
					httpHandler.Proxy = HttpConsole.LocalWebProxy;
				}
				SpiderHttpClient client = new SpiderHttpClient(httpHandler);
				var headers = client.DefaultRequestHeaders;
				headers.Host = PcWebApiProvider.HomePageUri.Host;
				headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
				headers.Add("Accept-Encoding", "br");
				headers.Add("Accept-Language", "zh-CN,zh;q=0.9");
				headers.Add("User-Agent", HttpConsole.DefaultPcUserAgent);
				return client;
			}

			private static async Task GetResponseData(SpiderHttpClient webClient, Uri targetUri, Uri cookieUri, HttpContent httpContent, CookieContainer cookieContainer, LaGouResponseDataCollection dataCollection)
			{
				var data = await GetResponseData(webClient, targetUri, cookieUri, httpContent, cookieContainer).ConfigureAwait(false);
				if (data != null)
				{
					dataCollection.AddResponseData(data);
				}
			}

			private static async Task<LaGouResponseData> GetResponseData(SpiderHttpClient webClient, Uri targetUri, Uri cookieUri, HttpContent content, CookieContainer cookieContainer)
			{
				string cookies = cookieContainer.GetCookieHeader(targetUri);
				var httpRequest = CreatePositionAjaxRequest(targetUri, content, cookies);
				var rspMsg = await webClient.SendAsync(httpRequest).ConfigureAwait(false);
				if (rspMsg == null || !rspMsg.IsSuccessStatusCode)
				{
					return null;
				}
				string text = await rspMsg.ToTextAsync().ConfigureAwait(false);
				ShowLogInfo(text);
				if (string.IsNullOrEmpty(text))
				{
					return null;
				}
				if (text.Contains("频繁"))
				{
					return null;
				}
				return PcWebApiProvider.CreateResponseData(text);
			}

			private static async Task ResetHttpClientCookies(SpiderHttpClient webClient, Uri targetUri)
			{
				MakeCookiesExpired(webClient.CookieContainer, targetUri);
				try
				{
					var rspMsg = await webClient.GetAsync(targetUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
					rspMsg.EnsureSuccessStatusCode();
				}
				catch
				{
					throw;
				}
			}

			private static HttpRequestMessage CreatePositionAjaxRequest(Uri targetUri, HttpContent content, string cookies)
			{
				HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, targetUri)
				{
					Content = content
				};
				httpRequest.Headers.Add("Cookie", cookies);
				return httpRequest;
			}

			private static void MakeCookiesExpired(CookieContainer container, Uri targetUri)
			{
				var cookies = container.GetCookies(targetUri).Cast<Cookie>();
				foreach (var cookie in cookies)
				{
					cookie.Expired = true;
				}
			}
		}
	}
}