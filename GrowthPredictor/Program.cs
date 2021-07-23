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
			using var gnuplot = new Gnuplot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gnuplot", "bin", "gnuplot.exe"));
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
				if (!TryParseCommand(line, out var target, out var subActionArguments))
					continue;
				SubAction? validatedSubAction = null;
				var validationResults = new List<Dictionary<SubActionArgumentName, SubActionArgumentError>>();
				foreach (var subAction in SubActions.Where(x => x.Target == target))
				{
					validationResults.Add(subAction.ValidateArguments(subActionArguments));
					if (validationResults[^1].Count == 0)
					{
						validatedSubAction = subAction;
						break;
					}
				}
				if (validatedSubAction == null)
				{
					if (validationResults.Count > 0)
					{
						var bestFitValidationResult = validationResults.Aggregate((x, y) => x.Count < y.Count ? x : y);
						foreach (var kvp in bestFitValidationResult.OrderBy(x => x.Key))
						{
							if (kvp.Value == SubActionArgumentError.RequiredParameterMissing)
								Console.WriteLine($"{kvp.Key} is required.");
							else if (kvp.Value == SubActionArgumentError.UnnecessaryArgumentFound)
								Console.WriteLine($"{kvp.Key} is unnecessary.");
							else
								Console.WriteLine($"{kvp.Key} expects point but got range.");
						}
					}
					else
						Console.WriteLine("Unknown target value.");
					continue;
				}
				validatedSubAction.Invoke(gnuplot, subActionArguments);
			}
		}

		static readonly IReadOnlyList<SubAction> SubActions = new[]
		{
			new SubAction("height", new Action<Gnuplot, int>((g, age) => PlotGrowthDistribution(g, x => x.Height, age))),
			new SubAction("weight", new Action<Gnuplot, int>((g, age) => PlotGrowthDistribution(g, x => x.Weight, age))),
			new SubAction("age"   , new Action<Gnuplot, ContinuousRange>((g, height) => PlotAgeGroupProbabilities(g, x => x.Height, height))),
			new SubAction("age"   , new Action<Gnuplot, ContinuousRange>((g, weight) => PlotAgeGroupProbabilities(g, x => x.Weight, weight))),
			new SubAction("lhs"   , new Action<int, decimal>((age, height) => ComputeLhs(age, x => x.Height, height))),
			new SubAction("lhs"   , new Action<int, decimal>((age, weight) => ComputeLhs(age, x => x.Weight, weight))),
			new SubAction("help"  , new Action(PrintHelp)),
		};

		static bool TryParseCommand(string command, [NotNullWhen(true)] out string? target, out SubActionArguments subActionArguments)
		{
			var components = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			target = default;
			subActionArguments = default;
			if (components.Length < 1)
			{
				Console.WriteLine("Cannot parse input command.");
				return false;
			}
			target = components[0];
			int? age = default;
			ContinuousRangeOrPoint? height = default;
			ContinuousRangeOrPoint? weight = default;
			foreach (var component in components.Skip(1))
			{
				if (component.EndsWith("yo"))
				{
					if (!int.TryParse(component[..^2].Trim(), out var result))
					{
						Console.WriteLine("Cannot convert input data to age.");
						return false;
					}
					age = result;
				}
				else if (component.EndsWith("cm"))
				{
					if (!ContinuousRangeOrPoint.TryParse(component[..^2].Trim(), out var result))
					{
						Console.WriteLine("Cannot convert input data to height.");
						return false;
					}
					height = result;
				}
				else if (component.EndsWith("kg"))
				{
					if (!ContinuousRangeOrPoint.TryParse(component[..^2].Trim(), out var result))
					{
						Console.WriteLine("Cannot convert input data to weight.");
						return false;
					}
					weight = result;
				}
			}
			subActionArguments = new SubActionArguments(age, height, weight);
			return true;
		}

		static void PrintHelp()
		{
			Console.WriteLine("Commands:");
			foreach (var subAction in SubActions)
			{
				Console.Write("  ");
				Console.Write(subAction.Target);
				if (subAction.UseAge)
					Console.Write(" <age>yo");
				if (subAction.UseHeight)
				{
					Console.Write(" <height>");
					if (subAction.IsHeightRange)
						Console.Write("[:<delta>]");
					Console.Write("cm");
				}
				if (subAction.UseWeight)
				{
					Console.Write(" <weight>");
					if (subAction.IsWeightRange)
						Console.Write("[:<delta>]");
					Console.Write("kg");
				}
				Console.WriteLine();
			}
			Console.WriteLine("Type ^Z followed by Enter to exit");
		}
		static void PlotGrowthDistribution(Gnuplot gnuplot, Func<AgeGroupGrowthData, GrowthDistribution> distributionSelector, int age)
		{
			if (!GrowthData.TryGetValue(age, out var data))
			{
				Console.WriteLine("Cannot find growth data corresponding to requested age.");
				return;
			}
			var distribution = distributionSelector(data);
			gnuplot.SetXRange((double)(distribution.Mean - distribution.StandardDeviation * 6), (double)(distribution.Mean + distribution.StandardDeviation * 6));
			gnuplot.SetYRange(0, double.NaN);
			gnuplot.Plot($"1 / sqrt(2 * pi * {distribution.StandardDeviation} ** 2) * exp(-(x - {distribution.Mean}) ** 2 / (2 * {distribution.StandardDeviation} ** 2))");
		}
		static void ComputeLhs(int age, Func<AgeGroupGrowthData, GrowthDistribution> distributionSelector, decimal value)
		{
			if (!GrowthData.TryGetValue(age, out var data))
			{
				Console.WriteLine("Cannot find growth data corresponding to requested age.");
				return;
			}
			Console.WriteLine($"{distributionSelector(data).GetCdf(value):E20}");
		}
		static void PlotAgeGroupProbabilities(Gnuplot gnuplot, Func<AgeGroupGrowthData, GrowthDistribution> distributionSelector, ContinuousRange range)
		{
			gnuplot.SetXRange(5, 17);
			gnuplot.SetYRange(0, double.NaN);
			gnuplot.Plot(GrowthData.Select(x => $"{x.Key} {distributionSelector(x.Value).GetProbability(range)}"), "w lp");
		}

		static readonly IReadOnlyDictionary<int, AgeGroupGrowthData> GrowthData = MakeGrowthData();

		static SortedDictionary<int, AgeGroupGrowthData> MakeGrowthData() => new()
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

	public enum SubActionArgumentName
	{
		None,
		Age,
		Height,
		Weight,
	}

	public readonly struct SubActionArguments : IEquatable<SubActionArguments>
	{
		public SubActionArguments(int? age, ContinuousRangeOrPoint? height, ContinuousRangeOrPoint? weight)
		{
			Age = age;
			Height = height;
			Weight = weight;
		}

		public int? Age { get; }
		public ContinuousRangeOrPoint? Height { get; }
		public ContinuousRangeOrPoint? Weight { get; }

		public bool Equals([AllowNull] SubActionArguments other) => Age == other.Age && Height == other.Height && Weight == other.Weight;
		public override bool Equals(object? obj) => obj is SubActionArguments other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Age, Height, Weight);

		public static bool operator ==(SubActionArguments left, SubActionArguments right) => left.Equals(right);
		public static bool operator !=(SubActionArguments left, SubActionArguments right) => !(left == right);
	}

	public readonly struct ContinuousRangeOrPoint : IEquatable<ContinuousRangeOrPoint>
	{
		public ContinuousRangeOrPoint(decimal value, int fractionDigit, decimal? range)
		{
			Value = value;
			FractionDigit = fractionDigit;
			Range = range;
		}

		public decimal Value { get; }
		public int FractionDigit { get; }
		public decimal? Range { get; }

		public bool Equals([AllowNull] ContinuousRangeOrPoint other) => Value == other.Value && Range == other.Range;
		public override bool Equals(object? obj) => obj is ContinuousRangeOrPoint other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Value, Range);

		public static bool operator ==(ContinuousRangeOrPoint left, ContinuousRangeOrPoint right) => left.Equals(right);
		public static bool operator !=(ContinuousRangeOrPoint left, ContinuousRangeOrPoint right) => !(left == right);

		public static bool TryParse(string text, out ContinuousRangeOrPoint result)
		{
			var colonIndex = text.IndexOf(':');
			var beforeColon = colonIndex < 0 ? text : text[0..colonIndex].Trim();
			var afterColon = colonIndex < 0 ? "" : text[(colonIndex + 1)..].Trim();
			if (!decimal.TryParse(beforeColon, out var value))
			{
				result = default;
				return false;
			}
			var fractionDigit = 0;
			var decimalPointIndex = beforeColon.IndexOf('.');
			if (decimalPointIndex >= 0)
				fractionDigit = beforeColon.Length - decimalPointIndex - 1;
			decimal? range = null;
			if (!string.IsNullOrEmpty(afterColon))
			{
				if (!decimal.TryParse(afterColon, out var rangeValue))
				{
					result = default;
					return false;
				}
				range = rangeValue;
			}
			result = new ContinuousRangeOrPoint(value, fractionDigit, range);
			return true;
		}
	}

	public enum SubActionArgumentError
	{
		None,
		RequiredParameterMissing,
		UnnecessaryArgumentFound,
		PointExpectedButGotRange,
	}

	public class SubAction
	{
		public SubAction(string target, Delegate action)
		{
			Target = target;
			m_Action = action;
			var parameters = action.Method.GetParameters();
			for (var i = 0; i < parameters.Length; i++)
			{
				if (parameters[i].ParameterType == typeof(Gnuplot))
				{
					if (m_GnuplotIndex >= 0)
						throw new ArgumentException($"Argument must be mapped to single delegate parameter: gnuplot -> {parameters[i].Name}", nameof(action));
					m_GnuplotIndex = i;
				}
				else if (parameters[i].Name == "age" && parameters[i].ParameterType == typeof(int))
					m_AgeIndex = i;
				else if (parameters[i].Name == "height" && parameters[i].ParameterType == typeof(ContinuousRange))
				{
					m_HeightIndex = i;
					IsHeightRange = true;
				}
				else if (parameters[i].Name == "height" && parameters[i].ParameterType == typeof(decimal))
				{
					m_HeightIndex = i;
					IsHeightRange = false;
				}
				else if (parameters[i].Name == "weight" && parameters[i].ParameterType == typeof(ContinuousRange))
				{
					m_WeightIndex = i;
					IsWeightRange = true;
				}
				else if (parameters[i].Name == "weight" && parameters[i].ParameterType == typeof(decimal))
				{
					m_WeightIndex = i;
					IsWeightRange = false;
				}
				else
					throw new ArgumentException($"Unmappable delegate parameter: {parameters[i].Name}", nameof(action));
			}
		}

		readonly Delegate m_Action;
		readonly int m_GnuplotIndex = -1;
		readonly int m_AgeIndex = -1;
		readonly int m_HeightIndex = -1;
		readonly int m_WeightIndex = -1;

		public bool UseAge => m_AgeIndex >= 0;
		public bool UseHeight => m_HeightIndex >= 0;
		public bool IsHeightRange { get; }
		public bool UseWeight => m_WeightIndex >= 0;
		public bool IsWeightRange { get; }
		public string Target { get; }

		public Dictionary<SubActionArgumentName, SubActionArgumentError> ValidateArguments(SubActionArguments subActionArguments)
		{
			var errors = new Dictionary<SubActionArgumentName, SubActionArgumentError>();
			Span<bool> itemUsages = stackalloc[] { UseAge, UseHeight, UseWeight };
			Span<bool> actualArgumentUsages = stackalloc[] { subActionArguments.Age.HasValue, subActionArguments.Height.HasValue, subActionArguments.Weight.HasValue };
			Span<bool> itemIsRanges = stackalloc[] { false, IsHeightRange, IsWeightRange };
			Span<bool> actualArgumentIsRanges = stackalloc[] { false, subActionArguments.Height.HasValue && subActionArguments.Height.Value.Range.HasValue, subActionArguments.Weight.HasValue && subActionArguments.Weight.Value.Range.HasValue };
			for (var i = 0; i < actualArgumentUsages.Length; ++i)
			{
				if (itemUsages[i] && !actualArgumentUsages[i])
					errors.Add((SubActionArgumentName)(i + 1), SubActionArgumentError.RequiredParameterMissing);
				if (!itemUsages[i] && actualArgumentUsages[i])
					errors.Add((SubActionArgumentName)(i + 1), SubActionArgumentError.UnnecessaryArgumentFound);
				if (!itemIsRanges[i] && actualArgumentIsRanges[i])
					errors.TryAdd((SubActionArgumentName)(i + 1), SubActionArgumentError.PointExpectedButGotRange);
			}
			return errors;
		}
		public void Invoke(Gnuplot gnuplot, SubActionArguments arguments)
		{
			var args = new object[Math.Max(Math.Max(Math.Max(m_GnuplotIndex, m_AgeIndex), m_HeightIndex), m_WeightIndex) + 1];
			if (m_GnuplotIndex >= 0)
				args[m_GnuplotIndex] = gnuplot;
			if (m_AgeIndex >= 0)
				args[m_AgeIndex] = arguments.Age.GetValueOrDefault();
			if (m_HeightIndex >= 0)
			{
				var height = arguments.Height.GetValueOrDefault();
				args[m_HeightIndex] = IsHeightRange ? new ContinuousRange(height) : height.Value;
			}
			if (m_WeightIndex >= 0)
			{
				var weight = arguments.Weight.GetValueOrDefault();
				args[m_WeightIndex] = IsWeightRange ? new ContinuousRange(weight) : weight.Value;
			}
			m_Action.DynamicInvoke(args);
		}
	}

	public readonly struct ContinuousRange : IEquatable<ContinuousRange>
	{
		public ContinuousRange(ContinuousRangeOrPoint rangeOrPoint)
		{
			Minimum = rangeOrPoint.Value;
			decimal range;
			if (rangeOrPoint.Range is not null)
				range = rangeOrPoint.Range.Value;
			else
			{
				range = 1;
				for (var i = 0; i < rangeOrPoint.FractionDigit; i++)
					range /= 10;
			}
			Maximum = Minimum + range;
		}

		public decimal Minimum { get; }
		public decimal Maximum { get; }

		public bool Equals([AllowNull] ContinuousRange other) => Minimum == other.Minimum && Maximum == other.Maximum;
		public override bool Equals(object? obj) => obj is ContinuousRange other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Minimum, Maximum);

		public static bool operator ==(ContinuousRange left, ContinuousRange right) => left.Equals(right);
		public static bool operator !=(ContinuousRange left, ContinuousRange right) => !(left == right);
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

	public readonly struct GrowthDistribution : IEquatable<GrowthDistribution>
	{
		public GrowthDistribution(decimal mean, decimal standardDeviation)
		{
			Mean = mean;
			StandardDeviation = standardDeviation;
		}

		public decimal Mean { get; }
		public decimal StandardDeviation { get; }

		static readonly double Sqrt2 = Math.Sqrt(2);

		public double GetProbability(ContinuousRange range)
		{
			var maxV = MathNet.Numerics.SpecialFunctions.Erf((double)(range.Maximum - Mean) / (double)StandardDeviation / Sqrt2);
			var minV = MathNet.Numerics.SpecialFunctions.Erf((double)(range.Minimum - Mean) / (double)StandardDeviation / Sqrt2);
			return (maxV - minV) / 2;
		}
		public double GetCdf(decimal value) => (1 + MathNet.Numerics.SpecialFunctions.Erf((double)(value - Mean) / (double)StandardDeviation / Sqrt2)) / 2;

		public bool Equals([AllowNull] GrowthDistribution other) => Mean == other.Mean && StandardDeviation == other.StandardDeviation;
		public override bool Equals(object? obj) => obj is GrowthDistribution other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Mean, StandardDeviation);

		public static bool operator ==(GrowthDistribution left, GrowthDistribution right) => left.Equals(right);
		public static bool operator !=(GrowthDistribution left, GrowthDistribution right) => !(left == right);
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

		Process? m_Process;

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
