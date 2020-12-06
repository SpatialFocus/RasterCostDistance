// <copyright file="Program.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace RasterCostDistance
{
	using System;
	using System.Diagnostics;
	using System.IO;
	using System.Threading;
	using System.Threading.Tasks;

	public sealed class Program
	{
		private static string Extension => "tif";

		private static string InputDirectory { get; } = @"data";

		private static string InputFileName { get; } = "settlements_clipped";

		private static string InputFilePath { get; } = @$"{Program.InputDirectory}/{Program.InputFileName}.{Program.Extension}";

		// Maximum cost distance, areas further away will be filled with this value
		// Use 0 to disable a limit
		private static int Maximum => 250;

		// Consider N4 or N8 neighborhood for calculating distance
		// N4 only considers left, right, top and bottom neighbors; N8 adds diagonals
		private static Func<Raster, int, int, int> NeighborFunction { get; } = Program.UpdateNeighborsHybrid;

		private static string OutputDirectory { get; } = @"data/results";

		private static string OutputFileName { get; } = "settlements_clipped_250max_d";

		private static string OutputFilePath { get; } = @$"{Program.OutputDirectory}/{Program.OutputFileName}.{Program.Extension}";

		private static string[] OutputOptions { get; } = { "TFW=YES", "COMPRESS=DEFLATE" };

		public static void Main()
		{
			Stopwatch stopwatch = Stopwatch.StartNew();

			Raster raster = Raster.Load(Program.InputFilePath);

			if (raster is null)
			{
				Console.WriteLine("Raster not found or unreadable. Exiting...");
				return;
			}

			Console.WriteLine($"{stopwatch.Elapsed} -> Raster loaded");

			int changes;
			int iteration = 1;

			do
			{
				changes = Program.ExpandToNeighbors(raster, iteration);
				Console.WriteLine($"{stopwatch.Elapsed} -> Iteration {iteration}: {changes} changes.");

				iteration++;
			}
			while (changes > 0 && (Program.Maximum == 0 || iteration < Program.Maximum));

			if (changes != 0 && Program.Maximum > 0)
			{
				changes = Program.FillRemaining(raster);
				Console.WriteLine($"{stopwatch.Elapsed} -> Remaining cells updated to {Program.Maximum}: {changes} changes.");
			}

			if (!Directory.Exists(Program.OutputDirectory))
			{
				Directory.CreateDirectory(Program.OutputDirectory);
			}

			raster.Write(Program.OutputFilePath, Program.OutputOptions);

			if (File.Exists($@"{Program.InputDirectory}/{Program.InputFileName}.tfw"))
			{
				File.Copy($@"{Program.InputDirectory}/{Program.InputFileName}.tfw",
					$@"{Program.OutputDirectory}/{Program.OutputFileName}.tfw", true);
			}

			stopwatch.Stop();
			Console.WriteLine($"{stopwatch.Elapsed} -> Raster saved");
		}

		private static int ExpandToNeighbors(Raster raster, int currentMax)
		{
			int changes = 0;

			int newValue = currentMax + 1;

			if (Program.Maximum > 0 && newValue > Program.Maximum)
			{
				newValue = Program.Maximum;
				currentMax = Program.Maximum;
			}

			Parallel.For(0, raster.Cells.Length, i =>
			{
				if (raster.Cells[i] == currentMax)
				{
					int c = Program.NeighborFunction(raster, i, newValue);
					Interlocked.Add(ref changes, c);
				}
			});

			return changes;
		}

		private static int FillRemaining(Raster raster)
		{
			int changes = 0;

			Parallel.For(0, raster.Cells.Length, i =>
			{
				if (raster.Cells[i] == 0)
				{
					int originalValue = Interlocked.CompareExchange(ref raster.Cells[i], Program.Maximum, 0);

					if (originalValue == 0)
					{
						Interlocked.Increment(ref changes);
					}
				}
			});

			return changes;
		}

		// Update the neighbor if it still has its default setting (0)
		private static int UpdateNeighbor(Raster raster, int x, int y, int newValue)
		{
			int i = raster.RowsColsToIndex(x, y);

			if (raster.Cells[i] == 0)
			{
				// Set newValue if array item is still 0 (thread safe)
				int originalValue = Interlocked.CompareExchange(ref raster.Cells[i], newValue, 0);

				if (originalValue == 0)
				{
					return 1;
				}
			}

			return 0;
		}

		private static int UpdateNeighborsHybrid(Raster raster, int i, int newValue)
		{
			return newValue % 2 == 0 ? Program.UpdateNeighborsN4(raster, i, newValue) : Program.UpdateNeighborsN8(raster, i, newValue);
		}

		private static int UpdateNeighborsN4(Raster raster, int i, int newValue)
		{
			int changes = 0;
			(int x, int y) = raster.IndexToRowsCols(i);

			if (x > 0)
			{
				changes += Program.UpdateNeighbor(raster, x - 1, y, newValue);
			}

			if (y > 0)
			{
				changes += Program.UpdateNeighbor(raster, x, y - 1, newValue);
			}

			if (y < raster.Rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x, y + 1, newValue);
			}

			if (x < raster.Cols - 1)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y, newValue);
			}

			return changes;
		}

		private static int UpdateNeighborsN8(Raster raster, int i, int newValue)
		{
			int changes = Program.UpdateNeighborsN4(raster, i, newValue);
			(int x, int y) = raster.IndexToRowsCols(i);

			if (x > 0 && y > 0)
			{
				changes += Program.UpdateNeighbor(raster, x - 1, y - 1, newValue);
			}

			if (x > 0 && y < raster.Rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x - 1, y + 1, newValue);
			}

			if (x < raster.Cols - 1 && y > 0)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y - 1, newValue);
			}

			if (x < raster.Cols - 1 && y < raster.Rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y + 1, newValue);
			}

			return changes;
		}
	}
}