using Microsoft.Extensions.Logging;
using PurpleTechnology.Watchdog.DealsMonitor.Config;
using PurpleTechnology.Watchdog.DealsMonitor.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PurpleTechnology.Watchdog.DealsMonitor
{
	public class DealProcessor<TServerId>
	{
		//Indexing data by OpenTime and BalanceToVolumeRatio
		private readonly Dictionary<string, SortedDictionary<long, SortedDictionary<decimal, Deal<TServerId>>>> _indexedDeals = new Dictionary<string, SortedDictionary<long, SortedDictionary<decimal, Deal<TServerId>>>>();

		//Be aware It does not persist data!
		//For more robust solution would be better to decouple producer/consumer into microservice and keep small SQL DB (e.g. SqlLite) for each replica of DealProcessor service
		public async Task Watch(IAsyncEnumerable<Deal<TServerId>> data,
								ILogger logger,
								DealsMonitorConfig dealsMonitorconfig,
								CancellationToken stoppingToken
			)
		{

			await foreach (var deal in data) {
				 stoppingToken.ThrowIfCancellationRequested();

				if (!_indexedDeals.ContainsKey(deal.Symbol)) {
					_indexedDeals.Add(deal.Symbol, CreateIndexedData(deal));
					continue;
				}
				var openTimeMax = deal.OpenTime + dealsMonitorconfig.OpenTimeDelta.TotalSeconds;
				var openTimeMin = deal.OpenTime - dealsMonitorconfig.OpenTimeDelta.TotalSeconds;
				var volumeToBalanceMax = deal.VolumeToBalanceRate + dealsMonitorconfig.VolumeToBalanceRatio;
				var volumeToBalanceMin = deal.VolumeToBalanceRate - dealsMonitorconfig.VolumeToBalanceRatio;
				var firstRelevantOpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ((long)dealsMonitorconfig.CacheTresholdTimeDelta.TotalSeconds);
				var indexedDataForSymbol = _indexedDeals[deal.Symbol];

				CleanFromExpiredData(firstRelevantOpenTime, indexedDataForSymbol);

				Deal<TServerId> firstSimilarDeal = null;

				foreach (var openTimeIndex in indexedDataForSymbol) { 
					
					if (openTimeIndex.Key < openTimeMin) continue;
					if (openTimeIndex.Key > openTimeMax) break;

					foreach (var volumeToBalanceIndex in indexedDataForSymbol[openTimeIndex.Key]) {
						
						if (volumeToBalanceIndex.Key <= volumeToBalanceMin) continue;
						if (volumeToBalanceIndex.Key >= volumeToBalanceMax) break;

						firstSimilarDeal = indexedDataForSymbol[openTimeIndex.Key][volumeToBalanceIndex.Key];
						break;
					}

					if (!(firstSimilarDeal is null)) {
						logger.LogWarning($"Similar order found. Order {deal.Order} on server {deal.ServerId}.");
						break;
					}
				};
				TryToAddDealToIndexedData(indexedDataForSymbol, deal);
			}
		}

		private void CleanFromExpiredData(long firstRelevantOpenTime, SortedDictionary<long, SortedDictionary<decimal, Deal<TServerId>>> indexedDataForSymbol)
		{
			var entriesToDelete = new List<long>();
			foreach (var openTimeIndex in indexedDataForSymbol) {
				
				if(openTimeIndex.Key > firstRelevantOpenTime) break;
				entriesToDelete.Add(openTimeIndex.Key);
			}
			entriesToDelete.ForEach(e => indexedDataForSymbol.Remove(e));
		}

		private void TryToAddDealToIndexedData(SortedDictionary<long, SortedDictionary<decimal, Deal<TServerId>>> indexedDataForSymbol, Deal<TServerId> deal)
		{
			if (!indexedDataForSymbol.ContainsKey(deal.OpenTime)) {

				var volumeToBalanceIndex = new SortedDictionary<decimal, Deal<TServerId>> ();
				volumeToBalanceIndex.Add(deal.VolumeToBalanceRate, deal);
				indexedDataForSymbol.Add(deal.OpenTime, volumeToBalanceIndex);
				return;
			}

			var indexedDataOpenTime = indexedDataForSymbol[deal.OpenTime];

			if (!indexedDataOpenTime.ContainsKey(deal.VolumeToBalanceRate)) {

				indexedDataOpenTime.Add(deal.VolumeToBalanceRate, deal);

			}
		}

		private SortedDictionary<long, SortedDictionary<decimal, Deal<TServerId>>> CreateIndexedData(Deal<TServerId> deal)
		{
			var volumeToBalanceIndex = new SortedDictionary<decimal, Deal<TServerId>> ();
			volumeToBalanceIndex.Add(deal.VolumeToBalanceRate, deal);
			var openTimeIndex = new SortedDictionary<long, SortedDictionary<decimal, Deal<TServerId>>>();
			openTimeIndex.Add(deal.OpenTime, volumeToBalanceIndex);
			return openTimeIndex;
		}
	}
}
