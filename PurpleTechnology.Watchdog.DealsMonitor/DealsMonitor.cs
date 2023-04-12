using Microsoft.Extensions.Logging;
using PurpleTechnology.MT5Wrapper.Abstractions;
using PurpleTechnology.MT5Wrapper.Data;
using PurpleTechnology.MT5Wrapper.Events;
using PurpleTechnology.Watchdog.Abstractions;
using PurpleTechnology.Watchdog.Config;
using PurpleTechnology.Watchdog.DealsMonitor.Config;
using PurpleTechnology.Watchdog.DealsMonitor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PurpleTechnology.Watchdog.DealsMonitor
{
	//More resilient solution can be done by decouple producer/consumer into microservices
	//for purpuse of this the assignment I just demonstrate parellel processing and using complex datastructure which 
	//mimic table indexing
	public class DealsMonitor<TServerId> : IMonitor
	{
		private readonly ILogger<DealsMonitor<TServerId>> _logger;
		private readonly DealsMonitorConfig _dealsMonitorConfig;
		private readonly IMT5ApiFactory<TServerId> _iMT5ApiFactory;
		private readonly ServersConfig<TServerId> _serversConfig;
		private readonly Dictionary<MT5DealAction, Channel<Deal<TServerId>>> _channels;
		private readonly Dictionary<TServerId, (IMT5Api<TServerId>, IMT5Api<TServerId>)> _mt5Apis = new Dictionary<TServerId, (IMT5Api<TServerId>, IMT5Api<TServerId>)>();

		public DealsMonitor(ILogger<DealsMonitor<TServerId>> logger,
			DealsMonitorConfig dealsMonitorConfig,
			ServersConfig<TServerId> serversConfig,
			IMT5ApiFactory<TServerId> iMT5ApiFactory
			)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_dealsMonitorConfig = dealsMonitorConfig ?? throw new ArgumentNullException(nameof(dealsMonitorConfig));
			_serversConfig = serversConfig ?? throw new ArgumentNullException(nameof(serversConfig));
			_iMT5ApiFactory = iMT5ApiFactory ?? throw new ArgumentNullException(nameof(iMT5ApiFactory));
			
			//Depend on desire level of parallelism key can be easily modified e.g. action + symbol
			_channels = new Dictionary<MT5DealAction, Channel<Deal<TServerId>>>() {
				{MT5DealAction.Sell, Channel.CreateBounded<Deal<TServerId>>(100)},
				{MT5DealAction.Buy, Channel.CreateBounded<Deal<TServerId>>(100)}
			};
		}

		public async void WriteToChannel(object source, DealEventArgs args)
		{
			var mt5Deal = args.Deal;

			if (mt5Deal.Action == MT5DealAction.Sell) {

				var server = (IMT5Api<TServerId>) source;
				Deal<TServerId> monitorDeal = Mt5DealToMonitorDeal(server.ServerId, mt5Deal);
				await _channels[MT5DealAction.Sell].Writer.WriteAsync(monitorDeal);

			}
			else if (mt5Deal.Action == MT5DealAction.Buy) {

				var server = (IMT5Api<TServerId>) source;
				Deal<TServerId> monitorDeal = Mt5DealToMonitorDeal(server.ServerId, mt5Deal);
				await _channels[MT5DealAction.Buy].Writer.WriteAsync(monitorDeal);
			}
		}

		private Deal<TServerId> Mt5DealToMonitorDeal(TServerId mt5Api, MT5Deal mt5Deal)
		{
			var userBalance = _mt5Apis[mt5Api].Item2.GetUserBalance(mt5Deal.Login);
			var volumeToBalanceRatio = mt5Deal.Volume/userBalance;
			var monitorDeal = new Deal<TServerId>{
				Action = mt5Deal.Action,
				Login = mt5Deal.Login,
				OpenTime = mt5Deal.OpenTime,
				Order = mt5Deal.Order,
				Symbol = mt5Deal.Symbol,
				VolumeToBalanceRate = volumeToBalanceRatio,
				ServerId = mt5Api
				
			};
			return monitorDeal;
		}

		//Can be extended to higher level of parallelism, by add new channels for Symbols
		//Be aware It does not persist data! In that case should be use message queue instead of channels and decople consumer/producer in microservises
		public async Task StartMonitoring(CancellationToken stoppingToken)
		{
			try {

				foreach (var serverConfig in _serversConfig.MT5ServersConfig) {
					var mt5ApiEventSubscription = _iMT5ApiFactory.Create();
					mt5ApiEventSubscription.Connect(serverConfig);

					var mt5ApiBalanceCheck = _iMT5ApiFactory.Create();
					mt5ApiBalanceCheck.Connect(serverConfig);

					_mt5Apis.Add(serverConfig.ServerId, (mt5ApiEventSubscription, mt5ApiBalanceCheck));

					mt5ApiEventSubscription.DealAddEvent += WriteToChannel;
				}

				List<Task> extractionTask = new List<Task>();

				extractionTask.Add(new DealProcessor<TServerId>().Watch(_channels[MT5DealAction.Sell].Reader.ReadAllAsync(), _logger, _dealsMonitorConfig, stoppingToken));
				extractionTask.Add(new DealProcessor<TServerId>().Watch(_channels[MT5DealAction.Buy].Reader.ReadAllAsync(), _logger, _dealsMonitorConfig, stoppingToken));
				await Task.WhenAll(extractionTask).ConfigureAwait(false);
			}
			finally {
				_mt5Apis.ToList().ForEach(keyValuePair => {
					keyValuePair.Value.Item1.Dispose();
					keyValuePair.Value.Item2.Dispose();
				});
			}
		}
	}
}
