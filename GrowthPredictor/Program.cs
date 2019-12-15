using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace GrowthPredictor
{
	class Program
	{
		static void Main()
		{
			var argumentNames = new[] { "Age", "Height", "Weight" };
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
				if (string.IsNullOrEmpty(line))
					continue;
				if (!ParseCommand(line, out var target, out var age, out var height, out var weight))
				{
					continue;
				}
				var subActionIndex = -1;
				Span<bool> actualArgumentUsages = stackalloc bool[] { age.HasValue, height.HasValue, weight.HasValue };
				for (var i = 0; i < SubActions.Count; ++i)
				{
					if (SubActions[i].Target != target)
						continue;
					subActionIndex = i;
					if (CheckArguments(actualArgumentUsages, i) == null)
						break;
				}
				if (subActionIndex < 0)
				{
					Console.WriteLine("Unknown target value.");
					continue;
				}
				var errorArgumentResult = CheckArguments(actualArgumentUsages, subActionIndex);
				if (errorArgumentResult != null)
				{
					if (errorArgumentResult.Value.Required)
						Console.WriteLine($"{argumentNames[errorArgumentResult.Value.Index]} is required.");
					else
						Console.WriteLine($"{argumentNames[errorArgumentResult.Value.Index]} is unnecessary.");
				}
				else
				{
					SubActions[subActionIndex].Action(gnuplot, age ?? default, height ?? default, weight ?? default);
				}
			}
		}

		static readonly IList<SubAction> SubActions = new SubAction[]
		{
			new SubAction("height", true , false, false, (g, age, _, __) => PredictHeightFromAge(g, age)),
			new SubAction("age"   , false, true , false, (g, _, height, __) => PlotAgeGroupProbabilities(g, x => x.Height, height.Minimum, height.Maximum)),
			new SubAction("age"   , false, false, true , (g, _, __, weight) => PlotAgeGroupProbabilities(g, x => x.Weight, weight.Minimum, weight.Maximum)),
			new SubAction("lhs"   , true,  true,  false, (_, age, height, __) => ComputeLhs(age, height))
		};

		static bool ParseCommand(string command, [NotNullWhen(true)] out string? target, out int? age, out ContinuousRange? height, out ContinuousRange? weight)
		{
			var components = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			target = default;
			age = default;
			height = default;
			weight = default;
			if (components.Length < 1)
			{
				Console.WriteLine("Cannot parse input command.");
				return false;
			}
			target = components[0];
			for (var i = 1; i < components.Length; ++i)
			{
				if (components[i].EndsWith("yo"))
				{
					if (!int.TryParse(components[i][..^2].Trim(), out var result))
					{
						Console.WriteLine("Cannot convert input data to age.");
						return false;
					}
					age = result;
				}
				else if (components[i].EndsWith("cm"))
				{
					if (!ContinuousRange.TryParse(components[i][..^2].Trim(), out var result))
					{
						Console.WriteLine("Cannot convert input data to height.");
						return false;
					}
					height = result;
				}
				else if (components[i].EndsWith("kg"))
				{
					if (!ContinuousRange.TryParse(components[i][..^2].Trim(), out var result))
					{
						Console.WriteLine("Cannot convert input data to weight.");
						return false;
					}
					weight = result;
				}
			}
			return true;
		}

		static (int Index, bool Required)? CheckArguments(Span<bool> actualArgumentUsages, int subActionIndex)
		{
			Span<bool> itemUsages = stackalloc bool[] { SubActions[subActionIndex].UseAge, SubActions[subActionIndex].UseHeight, SubActions[subActionIndex].UseWeight };
			for (var j = 0; j < actualArgumentUsages.Length; ++j)
			{
				if (itemUsages[j] && !actualArgumentUsages[j])
					return (j, true);
				if (!itemUsages[j] && actualArgumentUsages[j])
					return (j, false);
			}
			return null;
		}

		static void PredictHeightFromAge(Gnuplot gnuplot, int age)
		{
			if (!GrowthData.TryGetValue(age, out var data))
			{
				Console.WriteLine("Cannot find growth data corresponding to requested age.");
				return;
			}
			gnuplot.SetXRange((double)(data.Height.Mean - 30), (double)(data.Height.Mean + 30));
			gnuplot.SetYRange(0, double.NaN);
			gnuplot.Plot($"1 / sqrt(2 * pi * {data.Height.StandardDeviation} ** 2) * exp(-(x - {data.Height.Mean}) ** 2 / (2 * {data.Height.StandardDeviation} ** 2))");
		}

		static void ComputeLhs(int age, ContinuousRange height)
		{
			if (!GrowthData.TryGetValue(age, out var data))
			{
				Console.WriteLine("Cannot find growth data corresponding to requested age.");
				return;
			}
			Console.WriteLine($"{data.Height.GetCdf(height.Minimum):E20}");
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

	public readonly struct SubAction : IEquatable<SubAction>
	{
		public SubAction(string target, bool useAge, bool useHeight, bool useWeight, Action<Gnuplot, int, ContinuousRange, ContinuousRange> action)
		{
			Target = target;
			UseAge = useAge;
			UseHeight = useHeight;
			UseWeight = useWeight;
			Action = action;
		}

		public string Target { get; }
		public bool UseAge { get; }
		public bool UseHeight { get; }
		public bool UseWeight { get; }
		public Action<Gnuplot, int, ContinuousRange, ContinuousRange> Action { get; }

		public bool Equals([AllowNull] SubAction other) => Target == other.Target && UseAge == other.UseAge && UseHeight == other.UseHeight && UseWeight == other.UseWeight && Action == other.Action;
		public override bool Equals(object? obj) => obj is SubAction other && Equals(other);
		public override int GetHashCode() => Target.GetHashCode() ^ UseAge.GetHashCode() ^ UseHeight.GetHashCode() ^ UseWeight.GetHashCode() ^ Action.GetHashCode();
		public static bool operator ==(SubAction left, SubAction right) => left.Equals(right);
		public static bool operator !=(SubAction left, SubAction right) => !(left == right);
	}

	public readonly struct ContinuousRange : IEquatable<ContinuousRange>
	{
		public ContinuousRange(decimal minimum, decimal maximum)
		{
			Minimum = minimum;
			Maximum = maximum;
		}

		public decimal Minimum { get; }
		public decimal Maximum { get; }

		public bool Equals([AllowNull] ContinuousRange other) => Minimum == other.Minimum && Maximum == other.Maximum;
		public override bool Equals(object? obj) => obj is ContinuousRange other && Equals(other);
		public override int GetHashCode() => Minimum.GetHashCode() ^ Maximum.GetHashCode();
		public static bool operator ==(ContinuousRange left, ContinuousRange right) => left.Equals(right);
		public static bool operator !=(ContinuousRange left, ContinuousRange right) => !(left == right);

		public static bool TryParse(string text, out ContinuousRange result)
		{
			var colonIndex = text.IndexOf(':');
			var beforeColon = colonIndex < 0 ? text : text[0..colonIndex].Trim();
			var afterColon = colonIndex < 0 ? "" : text[(colonIndex + 1)..].Trim();
			if (!decimal.TryParse(beforeColon, out var value))
			{
				result = default;
				return false;
			}
			decimal range;
			if (!string.IsNullOrEmpty(afterColon))
			{
				if (!decimal.TryParse(afterColon, out range))
				{
					result = default;
					return false;
				}
			}
			else
			{
				range = 1;
				var decimalPointIndex = beforeColon.IndexOf('.');
				if (decimalPointIndex >= 0)
				{
					while (++decimalPointIndex < beforeColon.Length)
						range /= 10;
				}
			}
			result = new ContinuousRange(value, value + range);
			return true;
		}
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

		public double GetCdf(decimal value) => (1 + MathNet.Numerics.SpecialFunctions.Erf((double)(value - Mean) / (double)StandardDeviation / Sqrt2)) / 2;
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
			if (m_Process == null)
				throw new ObjectDisposedException(nameof(Gnuplot));
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

		Process? m_Process;

		static string GetRangeExpression(double min, double max) => string.Format("[{0}:{1}]", double.IsNaN(min) ? "*" : min.ToString(), double.IsNaN(max) ? "*" : max.ToString());

		public void Plot(string arguments) => Send($"plot {arguments}");

		public void Plot(IEnumerable<string> source, string arguments = "")
		{
			Send($"plot '-'{(string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments)}");
			foreach (var element in source)
				Send(element);
			Send("e");
		}

		public void SetXRange(double min, double max) => Send($"set xrange {GetRangeExpression(min, max)}");

		public void SetYRange(double min, double max) => Send($"set yrange {GetRangeExpression(min, max)}");

		public void Send(string message)
		{
			if (m_Process == null)
				throw new ObjectDisposedException(nameof(Gnuplot));
			m_Process.StandardInput.WriteLine(message);
		}
	}
}
