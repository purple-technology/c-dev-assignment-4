using PurpleTechnology.MT5Wrapper.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace PurpleTechnology.Watchdog.DealsMonitor.Models
{
	public class Deal<TServerId>
	{
		public ulong Login { get; set; }
		public string Symbol { get; set; }
		public MT5DealAction Action { get; set; }
		public ulong Order { get; set; }
		public long OpenTime { get; set; }
		public decimal VolumeToBalanceRate { get; set; }
		public TServerId ServerId { get; set; }
	}
}
