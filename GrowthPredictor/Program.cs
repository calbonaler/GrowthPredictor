using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GrowthPredictor
{
	class Program
	{
		static void Main(string[] args)
		{
			var gnuplot = new Gnuplot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gnuplot", "bin", "gnuplot.exe"));
			gnuplot.Send("unset key");
			Console.WriteLine("Welcome to Growth Predictor");
			Console.WriteLine();
			while (true)
			{
				Console.Write("> ");
				var line = Console.ReadLine();
				if (line == null)
					break;
				line = line.Trim();
				foreach (var component in line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (component.EndsWith("yo"))
					{
						if (!int.TryParse(component.Substring(0, component.Length - 2), out var yearOld))
						{
							Console.WriteLine("Cannot convert input data to age.");
							Console.WriteLine();
							goto Next;
						}
						if (!GrowthData.TryGetValue(yearOld, out var data))
						{
							Console.WriteLine("Cannot find growth data corresponding to requested age.");
							Console.WriteLine();
							goto Next;
						}
						gnuplot.SetXRange((double)(data.Height.Mean - 30), (double)(data.Height.Mean + 30));
						gnuplot.SetYRange(0, double.NaN);
						gnuplot.Plot($"1 / sqrt(2 * pi * {data.Height.StandardDeviation} ** 2) * exp(-(x - {data.Height.Mean}) ** 2 / (2 * {data.Height.StandardDeviation} ** 2))");
					}
					else if (component.EndsWith("cm"))
					{
						if (!TryParseValueAndRange(component.Substring(0, component.Length - 2), out var value, out var range))
						{
							Console.WriteLine("Cannot convert input data to height.");
							Console.WriteLine();
							goto Next;
						}
						PlotAgeGroupProbabilities(gnuplot, data => data.Height, value, value + range);
					}
					else if (component.EndsWith("kg"))
					{
						if (!TryParseValueAndRange(component.Substring(0, component.Length - 2), out var value, out var range))
						{
							Console.WriteLine("Cannot convert input data to weight.");
							Console.WriteLine();
							goto Next;
						}
						PlotAgeGroupProbabilities(gnuplot, data => data.Weight, value, value + range);
					}
					else
					{
						Console.WriteLine("Cannot parse input command");
						Console.WriteLine();
						goto Next;
					}
				}
			Next:;
			}
		}

		static bool TryParseValueAndRange(string text, out decimal value, out decimal range)
		{
			text = text.Trim();
			var colonIndex = text.IndexOf(':');
			var beforeColon = colonIndex < 0 ? text : text.Substring(0, colonIndex).Trim();
			var afterColon = colonIndex < 0 ? "" : text.Substring(colonIndex + 1).Trim();
			if (!decimal.TryParse(beforeColon, out value))
			{
				range = 0;
				return false;
			}
			if (!string.IsNullOrEmpty(afterColon))
				return decimal.TryParse(afterColon, out range);
			range = 1;
			var decimalPointIndex = beforeColon.IndexOf('.');
			if (decimalPointIndex >= 0)
			{
				while (++decimalPointIndex < beforeColon.Length)
					range /= 10;
			}
			return true;
		}

		static void PlotAgeGroupProbabilities(Gnuplot gnuplot, Func<AgeGroupGrowthData, GrowthDistribution> distributionSelector, decimal min, decimal max)
		{
			gnuplot.SetXRange(5, 17);
			gnuplot.SetYRange(0, double.NaN);
			var likelihoods = GrowthData.OrderBy(x => x.Key).Select(x => new KeyValuePair<int, double>(x.Key, distributionSelector(x.Value).GetProbability(min, max))).ToArray();
			gnuplot.Plot(likelihoods.Select(x => $"{x.Key} {x.Value}"), "w lp");
		}

		static readonly IReadOnlyDictionary<int, AgeGroupGrowthData> GrowthData = MakeGrowthData();

		static Dictionary<int, AgeGroupGrowthData> MakeGrowthData() => new Dictionary<int, AgeGroupGrowthData>()
		{
			{  5, new AgeGroupGrowthData((109.4m, 4.66m), (18.5m, 2.48m)) },
			{  6, new AgeGroupGrowthData((115.5m, 4.83m), (20.8m, 3.15m)) },
			{  7, new AgeGroupGrowthData((121.5m, 5.13m), (23.4m, 3.72m)) },
			{  8, new AgeGroupGrowthData((127.3m, 5.50m), (26.4m, 4.71m)) },
			{  9, new AgeGroupGrowthData((133.4m, 6.14m), (29.7m, 5.72m)) },
			{ 10, new AgeGroupGrowthData((140.1m, 6.77m), (33.9m, 6.85m)) },
			{ 11, new AgeGroupGrowthData((146.7m, 6.63m), (38.8m, 7.66m)) },
			{ 12, new AgeGroupGrowthData((151.8m, 5.90m), (43.6m, 7.95m)) },
			{ 13, new AgeGroupGrowthData((154.9m, 5.44m), (47.3m, 7.70m)) },
			{ 14, new AgeGroupGrowthData((156.5m, 5.30m), (49.9m, 7.41m)) },
			{ 15, new AgeGroupGrowthData((157.1m, 5.29m), (51.5m, 7.76m)) },
			{ 16, new AgeGroupGrowthData((157.6m, 5.32m), (52.6m, 7.72m)) },
			{ 17, new AgeGroupGrowthData((157.9m, 5.38m), (53.0m, 7.83m)) },
		};
	}

	public class AgeGroupGrowthData
	{
		public AgeGroupGrowthData((decimal mean, decimal standardDeviation) height, (decimal mean, decimal standardDeviation) weight)
		{
			Height = new GrowthDistribution(height.mean, height.standardDeviation);
			Weight = new GrowthDistribution(weight.mean, weight.standardDeviation);
		}

		public GrowthDistribution Height { get; }

		public GrowthDistribution Weight { get; }
	}

	public class GrowthDistribution
	{
		public GrowthDistribution(decimal mean, decimal standardDeviation)
		{
			Mean = mean;
			StandardDeviation = standardDeviation;
		}

		public decimal Mean { get; }

		public decimal StandardDeviation { get; }

		static readonly double Sqrt2 = Math.Sqrt(2);

		public double GetProbability(decimal min, decimal max)
		{
			var maxV = MathNet.Numerics.SpecialFunctions.Erf((double)(max - Mean) / (double)StandardDeviation / Sqrt2);
			var minV = MathNet.Numerics.SpecialFunctions.Erf((double)(min - Mean) / (double)StandardDeviation / Sqrt2);
			return (maxV - minV) / 2;
		}
	}

	public class Gnuplot : IDisposable
	{
		public Gnuplot(string path)
		{
			m_Process = new Process();
			m_Process.StartInfo.FileName = path;
			m_Process.StartInfo.UseShellExecute = false;
			m_Process.StartInfo.RedirectStandardInput = true;
			m_Process.Start();
		}

		public void Kill()
		{
			if (!m_Process.HasExited)
				m_Process.Kill();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing && m_Process != null)
			{
				Kill();
				m_Process.Dispose();
				m_Process = null;
			}
		}

		Process m_Process;

		static string GetRangeExpression(double min, double max) => string.Format("[{0}:{1}]", double.IsNaN(min) ? "*" : min.ToString(), double.IsNaN(max) ? "*" : max.ToString());

		public void Plot(string arguments) => Send($"plot {arguments}");

		public void Plot<T>(IEnumerable<T> source, string arguments = "") => Plot(source.Select(x => x.ToString()), arguments);

		public void Plot(IEnumerable<string> source, string arguments = "")
		{
			Send($"plot '-'{(string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments)}");
			foreach (var element in source)
				Send(element);
			Send("e");
		}

		public void SetXRange(double min, double max) => Send($"set xrange {GetRangeExpression(min, max)}");

		public void SetYRange(double min, double max) => Send($"set yrange {GetRangeExpression(min, max)}");

		public void Send(string message) => m_Process.StandardInput.WriteLine(message);
	}
}
