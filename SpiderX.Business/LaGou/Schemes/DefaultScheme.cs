﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SpiderX.Business.LaGou.DbContexts;

namespace SpiderX.Business.LaGou
{
	public sealed partial class LaGouBll
	{
		private class DefaultScheme : SchemeBase
		{
			public override async Task RunAsync(LaGouSearchParam searchParam)
			{
				var datas = await Collector.CollectAsync(searchParam);
				using (var context = new LaGouSqlServerContext())
				{
					context.Database.EnsureCreated();
					InsertData(context, c => c.Positions, datas.Positions.Values, p => p.PositionId);
					InsertData(context, c => c.Companies, datas.Companies.Values, p => p.CompanyId);
					InsertData(context, c => c.HrInfos, datas.HrInfos.Values, p => p.UserId);
					InsertData(context, c => c.HrDailyRecords, datas.HrDailyRecords.Values);
				}
			}

			private static void InsertData<T>(LaGouContext dbContext, Func<LaGouContext, DbSet<T>> dbSetSelector, ICollection<T> data) where T : class
			{
				var dbSet = dbSetSelector(dbContext);
				dbSet.AddRange(data);
				try
				{
					dbContext.SaveChanges();
				}
				catch (Exception ex)
				{
					ShowLogError(ex.Message);
				}
			}

			private static void InsertData<T, TKey>(LaGouContext dbContext, Func<LaGouContext, DbSet<T>> dbSetSelector, ICollection<T> data, Expression<Func<T, TKey>> keySelector) where T : class
			{
				var dbSet = dbSetSelector(dbContext);
				var keyHashSet = dbSet.Select(keySelector).ToHashSet();
				var keyFunc = keySelector.Compile();
				var newData = data.Where(p => !keyHashSet.Contains(keyFunc(p)));
				if (!newData.Any())
				{
					return;
				}
				dbSet.AddRange(newData);
				try
				{
					dbContext.SaveChanges();
				}
				catch (Exception ex)
				{
					ShowLogException(ex);
				}
			}
		}
	}
}