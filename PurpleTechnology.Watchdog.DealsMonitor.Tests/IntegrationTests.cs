using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Moq;
using PurpleTechnology.MT5Wrapper.Data;
using PurpleTechnology.Watchdog.DealsMonitor.Config;
using PurpleTechnology.Watchdog.DealsMonitor.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using Xunit;

namespace PurpleTechnology.Watchdog.DealsMonitor.Tests
{
	public class DealProcessorTests
    {

		[Fact]
		public async void TestVolumeToBalanceRationTresholdAsync() 
		{
			List<string> logMessages = new List<string>();
			Mock<ILogger> mockLogger = new Mock<ILogger>();

			var channel = Channel.CreateBounded<Deal<string>>(100);
			DealsMonitorConfig dealsMonitorConfig = new DealsMonitorConfig(){
				VolumeToBalanceRatio =  Decimal.Parse("0.05", CultureInfo. InvariantCulture),
				CacheTresholdTimeDelta = TimeSpan.FromSeconds(1000),
				OpenTimeDelta = TimeSpan.FromSeconds(20)
			};

			var deal1 = new Deal<string>() {
				Action = MT5DealAction.Sell,
				ServerId = "TestServer",
				Login = 123,
				OpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 2,
				Order = 1,
				Symbol = "EURCZK",
				VolumeToBalanceRate = 1
			};
			var deal2 = new Deal<string>() {
				Action = MT5DealAction.Sell,
				ServerId = "TestServer",
				Login = 123,
				OpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1,
				Order = 2,
				Symbol = "EURCZK",
				VolumeToBalanceRate = Decimal.Parse("1.04", CultureInfo. InvariantCulture)
			};
			var deal3 = new Deal<string>() {
				Action = MT5DealAction.Sell,
				ServerId = "TestServer",
				Login = 123,
				OpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
				Order = 3,
				Symbol = "EURCZK",
				VolumeToBalanceRate = Decimal.Parse("0.96", CultureInfo. InvariantCulture)
			};

			await channel.Writer.WriteAsync(deal1);
			await channel.Writer.WriteAsync(deal2);
			await channel.Writer.WriteAsync(deal3);

			channel.Writer.Complete();

			await new DealProcessor<string>().Watch(channel.Reader.ReadAllAsync(), mockLogger.Object, dealsMonitorConfig, new CancellationToken());

			//Assert.True(logMessages.Count == 1);
			string message1 = $"Similar order found. Order {deal2.Order} on server {deal2.ServerId}.";
			string message2 = $"Similar order found. Order {deal3.Order} on server {deal3.ServerId}.";

			mockLogger.Verify(
				x => x.Log(
					It.Is<LogLevel>(l => l == LogLevel.Warning),
					It.IsAny<EventId>(),
					It.Is<It.IsAnyType>((v, t) => v.ToString() == message1),
					It.IsAny<Exception>(),
					It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)), Times.Once);

			mockLogger.Verify(
				x => x.Log(
					It.Is<LogLevel>(l => l == LogLevel.Warning),
					It.IsAny<EventId>(),
					It.Is<It.IsAnyType>((v, t) => v.ToString() == message2),
					It.IsAny<Exception>(),
					It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)), Times.Once);

		}

		[Fact]
		public async void TestOpenDetlaTimeAsync()
		{
			List<string> logMessages = new List<string>();
			Mock<ILogger> mockLogger = new Mock<ILogger>();

			var channel = Channel.CreateBounded<Deal<string>>(100);
			DealsMonitorConfig dealsMonitorConfig = new DealsMonitorConfig(){
				VolumeToBalanceRatio =  Decimal.Parse("0.05", CultureInfo. InvariantCulture),
				CacheTresholdTimeDelta = TimeSpan.FromSeconds(1000),
				OpenTimeDelta = TimeSpan.FromSeconds(2)
			};

			var deal1 = new Deal<string>() {
				Action = MT5DealAction.Sell,
				ServerId = "TestServer",
				Login = 123,
				OpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 19,
				Order = 1,
				Symbol = "EURCZK",
				VolumeToBalanceRate = 1
			};
			var deal2 = new Deal<string>() {
				Action = MT5DealAction.Sell,
				ServerId = "TestServer",
				Login = 123,
				OpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 18,
				Order = 2,
				Symbol = "EURCZK",
				VolumeToBalanceRate = Decimal.Parse("1.04", CultureInfo. InvariantCulture)
			};
			var deal3 = new Deal<string>() {
				Action = MT5DealAction.Sell,
				ServerId = "TestServer",
				Login = 123,
				OpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
				Order = 3,
				Symbol = "EURCZK",
				VolumeToBalanceRate = Decimal.Parse("0.96", CultureInfo. InvariantCulture)
			};

			await channel.Writer.WriteAsync(deal1);
			await channel.Writer.WriteAsync(deal2);
			await channel.Writer.WriteAsync(deal3);

			channel.Writer.Complete();

			await new DealProcessor<string>().Watch(channel.Reader.ReadAllAsync(), mockLogger.Object, dealsMonitorConfig, new CancellationToken());

			//Assert.True(logMessages.Count == 1);
			string message1 = $"Similar order found. Order {deal2.Order} on server {deal2.ServerId}.";
			string message2 = $"Similar order found. Order {deal3.Order} on server {deal3.ServerId}.";

			mockLogger.Verify(
				x => x.Log(
					It.Is<LogLevel>(l => l == LogLevel.Warning),
					It.IsAny<EventId>(),
					It.Is<It.IsAnyType>((v, t) => v.ToString() == message1),
					It.IsAny<Exception>(),
					It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)), Times.Once);

			mockLogger.Verify(
				x => x.Log(
					It.Is<LogLevel>(l => l == LogLevel.Warning),
					It.IsAny<EventId>(),
					It.Is<It.IsAnyType>((v, t) => v.ToString() == message2),
					It.IsAny<Exception>(),
					It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)), Times.Never);

		}

		[Fact]
		public async void TestExpiredHitAsync()
		{
			List<string> logMessages = new List<string>();
			Mock<ILogger> mockLogger = new Mock<ILogger>();

			var channel = Channel.CreateBounded<Deal<string>>(100);
			DealsMonitorConfig dealsMonitorConfig = new DealsMonitorConfig(){
				VolumeToBalanceRatio =  Decimal.Parse("0.05", CultureInfo. InvariantCulture),
				CacheTresholdTimeDelta = TimeSpan.FromSeconds(10),
				OpenTimeDelta = TimeSpan.FromSeconds(20)
			};

			var deal1 = new Deal<string>() {
				Action = MT5DealAction.Sell,
				ServerId = "TestServer",
				Login = 123,
				OpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 29,
				Order = 1,
				Symbol = "EURCZK",
				VolumeToBalanceRate = 1
			};
			var deal2 = new Deal<string>() {
				Action = MT5DealAction.Sell,
				ServerId = "TestServer",
				Login = 123,
				OpenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1,
				Order = 2,
				Symbol = "EURCZK",
				VolumeToBalanceRate = 1
			};

			await channel.Writer.WriteAsync(deal1);
			await channel.Writer.WriteAsync(deal2);

			channel.Writer.Complete();

			await new DealProcessor<string>().Watch(channel.Reader.ReadAllAsync(), mockLogger.Object, dealsMonitorConfig, new CancellationToken());

			//Assert.True(logMessages.Count == 1);
			string message1 = $"Similar order found. Order {deal2.Order} on server {deal2.ServerId}.";

			mockLogger.Verify(
				x => x.Log(
					It.Is<LogLevel>(l => l == LogLevel.Warning),
					It.IsAny<EventId>(),
					It.Is<It.IsAnyType>((v, t) => true),
					It.IsAny<Exception>(),
					It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)), Times.Never);

		}
	}
}
