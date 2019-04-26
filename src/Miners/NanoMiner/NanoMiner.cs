﻿using MinerPlugin;
using MinerPluginToolkitV1;
using MinerPluginToolkitV1.ExtraLaunchParameters;
using NiceHashMinerLegacy.Common;
using NiceHashMinerLegacy.Common.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NanoMiner
{
    public class NanoMiner : MinerBase
    {
        private readonly string _uuid;

        private HttpClient _httpClient;
        private string _extraLaunchParameters = "";

        private int _apiPort;

        private AlgorithmType _algorithmType;

        private string _devices;

        public NanoMiner(string uuid)
        {
            _uuid = uuid;
        }

        protected virtual string AlgorithmName(AlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case AlgorithmType.GrinCuckaroo29:
                    return "Cuckaroo29";
                case AlgorithmType.CryptoNightR:
                    return "CryptoNightR";
                default:
                    return "";
            }
        }

        private double DevFee
        {
            get
            {
                switch (_algorithmType)
                {
                    case AlgorithmType.CryptoNightR:
                        return 1.0;
                    case AlgorithmType.GrinCuckaroo29:
                    default: return 2.0;
                }
            }
        }

        public override Tuple<string, string> GetBinAndCwdPaths()
        {
            var pluginRoot = Path.Combine(Paths.MinerPluginsPath(), _uuid);
            var pluginRootBins = Path.Combine(pluginRoot, "bins");
            var binPath = Path.Combine(pluginRootBins, "nanominer.exe");
            var binCwd = pluginRootBins;
            return Tuple.Create(binPath, binCwd);
        }

        protected override void Init()
        {
            var singleType = MinerToolkit.GetAlgorithmSingleType(_miningPairs);
            _algorithmType = singleType.Item1;
            bool ok = singleType.Item2;
            if (!ok) throw new InvalidOperationException("Invalid mining initialization");

            var orderedMiningPairs = _miningPairs.ToList();
            orderedMiningPairs.Sort((a, b) => a.Device.ID.CompareTo(b.Device.ID));
            _devices = string.Join(",", orderedMiningPairs.Select(p => p.Device.ID));
            //TODO this must be implemented
            if (MinerOptionsPackage != null)
            {
                // TODO add ignore temperature checks
                var generalParams = Parser.Parse(orderedMiningPairs, MinerOptionsPackage.GeneralOptions);
                var temperatureParams = Parser.Parse(orderedMiningPairs, MinerOptionsPackage.TemperatureOptions);
                _extraLaunchParameters = $"{generalParams} {temperatureParams}".Trim();
            }
        }

        public override Task<ApiData> GetMinerStatsDataAsync()
        {
            throw new NotImplementedException();
        }

        public async override Task<BenchmarkResult> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            int benchmarkTime;
            switch (benchmarkType)
            {
                case BenchmarkPerformanceType.Quick:
                    benchmarkTime = 20;
                    break;
                case BenchmarkPerformanceType.Precise:
                    benchmarkTime = 120;
                    break;
                default:
                    benchmarkTime = 60;
                    break;
            }

            var cl = CreateCommandLine(MinerToolkit.DemoUserBTC, _devices);
            var binPathBinCwdPair = GetBinAndCwdPaths();
            var binPath = binPathBinCwdPair.Item1;
            var binCwd = binPathBinCwdPair.Item2;
            var bp = new BenchmarkProcess(binPath, binCwd, cl, GetEnvironmentVariables());

            var benchHashes = 0d;
            var benchIters = 0;
            var benchHashResult = 0d;  // Not too sure what this is..
            var targetBenchIters = Math.Max(1, (int)Math.Floor(benchmarkTime / 20d));

            bp.CheckData = (string data) =>
            {
                var hashrateFoundPair = MinerToolkit.TryGetHashrateAfter(data, "Total speed:");
                var hashrate = hashrateFoundPair.Item1;
                var found = hashrateFoundPair.Item2;

                if (found)
                {
                    benchHashes += hashrate;
                    benchIters++;

                    benchHashResult = (benchHashes / benchIters) * (1 - DevFee * 0.01);
                }

                return new BenchmarkResult
                {
                    AlgorithmTypeSpeeds = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, benchHashResult) },
                    Success = benchIters >= targetBenchIters
                };
            };

            var benchmarkTimeout = TimeSpan.FromSeconds(benchmarkTime + 10);
            var benchmarkWait = TimeSpan.FromMilliseconds(500);
            var t = MinerToolkit.WaitBenchmarkResult(bp, benchmarkTimeout, benchmarkWait, stop);
            return await t;
        }

        protected override string MiningCreateCommandLine()
        {
            return CreateCommandLine(_username, _devices);
        }

        private string CreateCommandLine(string username, string deviceId)
        {
            _apiPort = MinersApiPortsManager.GetAvaliablePortInRange();

            var algo = AlgorithmName(_algorithmType);

            var url = StratumServiceHelpers.GetLocationUrl(_algorithmType, _miningLocation, NhmConectionType.NONE);
            var paths = GetBinAndCwdPaths();

            var configString = "";
            if (_extraLaunchParameters != "")
            {
                var arrayOfELP = _extraLaunchParameters.Split(' ');
                foreach (var elp in arrayOfELP)
                {
                    configString += $"{elp}\r\n";
                }
            }

            configString += $"mport=-{_apiPort}\r\nwatchdog=false\n\r\n\r[{algo}]\r\nwallet={username}\r\ndevices={_devices}\r\npool1={url}";
            File.WriteAllText(Path.Combine(paths.Item2,$"config_nh_{deviceId}.ini"), configString);
            return $"config_nh_{deviceId}.ini";
        }
    }
}
