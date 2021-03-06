﻿using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpiderX.Proxy;

namespace SpiderX.ProxyFetcher
{
	public sealed class XiciDailiProxyBll : ProxyBll
	{
		public XiciDailiProxyBll(ILogger logger, string[] runSetting, string dbConfigName, int version) : base(logger, runSetting, dbConfigName, version)
		{
		}

		internal override ProxyApiProvider ApiProvider { get; } = new XiciDailiProxyApiProvider();

		public override async Task RunAsync()
		{
			string caseName = ClassName;
			using var dbContext = ProxyDbContext.CreateInstance();
			var urls = ApiProvider.GetRequestUrls();
			using var webClient = ApiProvider.CreateWebClient();
			var entities = await GetProxyEntitiesAsync(webClient, HttpMethod.Get, urls, urls.Count * 32);
			if (entities.Count < 1)
			{
				return;
			}
			entities.ForEach(e => e.Source = caseName);
			ShowLogInfo("CollectCount: " + entities.Count.ToString());
			int insertCount = dbContext.InsertProxyEntities(entities);
			ShowLogInfo("InsertCount: " + insertCount.ToString());
		}
	}
}