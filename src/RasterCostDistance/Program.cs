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
	using MaxRev.Gdal.Core;
	using OSGeo.GDAL;

	public sealed class Program
	{
		private static int cols;

		private static Driver driver;

		private static bool hasNoDataValue;

		private static double noDataValue;

		private static string projection;

		private static int rows;

		private static string Extension => "tif";

		private static string InputDirectory { get; } = @"data";

		private static string InputFileName { get; } = "settlements_clipped";

		private static string InputFilePath { get; } = @$"{Program.InputDirectory}/{Program.InputFileName}.{Program.Extension}";

		// Maximum cost distance, areas further away will be filled with this value
		// Use 0 to disable a limit
		private static int Maximum => 250;

		private static string OutputDirectory { get; } = @"data/results";

		private static string OutputFileName { get; } = "settlements_clipped_250max_b";

		private static string OutputFilePath { get; } = @$"{Program.OutputDirectory}/{Program.OutputFileName}.{Program.Extension}";

		private static string[] OutputOptions { get; } = { "TFW=YES", "COMPRESS=DEFLATE" };

		public static void Main()
		{
			GdalBase.ConfigureAll();
			Stopwatch stopwatch = Stopwatch.StartNew();

			int[] raster = Program.LoadRaster();

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

			Program.WriteRaster(raster);

			stopwatch.Stop();
			Console.WriteLine($"{stopwatch.Elapsed} -> Raster saved");
		}

		private static int ExpandToNeighbors(int[] raster, int currentMax)
		{
			int changes = 0;

			int newValue = currentMax + 1;

			if (Program.Maximum > 0 && newValue > Program.Maximum)
			{
				newValue = Program.Maximum;
				currentMax = Program.Maximum;
			}

			Parallel.For(0, raster.Length, i =>
			{
				if (raster[i] == currentMax)
				{
					int c = Program.UpdateNeighbors(raster, i, newValue);
					Interlocked.Add(ref changes, c);
				}
			});

			return changes;
		}

		private static int FillRemaining(int[] raster)
		{
			int changes = 0;

			Parallel.For(0, raster.Length, i =>
			{
				if (raster[i] == 0)
				{
					int originalValue = Interlocked.CompareExchange(ref raster[i], Program.Maximum, 0);

					if (originalValue == 0)
					{
						Interlocked.Increment(ref changes);
					}
				}
			});

			return changes;
		}

		private static (int x, int y) IndexToRowsCols(int i) => (i % Program.cols, i / Program.cols);

		private static int[] LoadRaster()
		{
			if (!File.Exists(Program.InputFilePath))
			{
				return null;
			}

			try
			{
				using Dataset dataset = Gdal.Open(Program.InputFilePath, Access.GA_ReadOnly);
				Band band = dataset.GetRasterBand(1);

				// Get and store raster metadata
				Program.driver = dataset.GetDriver();
				Program.projection = dataset.GetProjection();
				Program.cols = band.XSize;
				Program.rows = band.YSize;
				band.GetNoDataValue(out Program.noDataValue, out int hasVal);
				Program.hasNoDataValue = hasVal == 1;

				int[] buffer = new int[band.XSize * band.YSize];
				band.ReadRaster(0, 0, band.XSize, band.YSize, buffer, band.XSize, band.YSize, 0, 0);

				return buffer;
			}
#pragma warning disable CA1031 // Do not catch general exception types
			catch (Exception e)
			{
				Console.WriteLine(e);
				return null;
			}
#pragma warning restore CA1031 // Do not catch general exception types
		}

		private static int RowsColsToIndex(int x, int y) => x + (y * Program.cols);

		// Update the neighbor if it still has its default setting (0)
		private static int UpdateNeighbor(int[] raster, int x, int y, int newValue)
		{
			int i = Program.RowsColsToIndex(x, y);

			if (raster[i] == 0)
			{
				// Set newValue if array item is still 0 (thread safe)
				int originalValue = Interlocked.CompareExchange(ref raster[i], newValue, 0);

				if (originalValue == 0)
				{
					return 1;
				}
			}

			return 0;
		}

		// Updating N8 neighbors
		private static int UpdateNeighbors(int[] raster, int i, int newValue)
		{
			int changes = 0;
			(int x, int y) = Program.IndexToRowsCols(i);

			if (x > 0 && y > 0)
			{
				changes += Program.UpdateNeighbor(raster, x - 1, y - 1, newValue);
			}

			if (x > 0)
			{
				changes += Program.UpdateNeighbor(raster, x - 1, y, newValue);
			}

			if (x > 0 && y < Program.rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x - 1, y + 1, newValue);
			}

			if (y > 0)
			{
				changes += Program.UpdateNeighbor(raster, x, y - 1, newValue);
			}

			if (y < Program.rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x, y + 1, newValue);
			}

			if (x < Program.cols - 1 && y > 0)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y - 1, newValue);
			}

			if (x < Program.cols - 1)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y, newValue);
			}

			if (x < Program.cols - 1 && y < Program.rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y + 1, newValue);
			}

			return changes;
		}

		private static void WriteRaster(int[] raster)
		{
			if (!Directory.Exists(Program.OutputDirectory))
			{
				Directory.CreateDirectory(Program.OutputDirectory);
			}

			if (File.Exists(Program.OutputFilePath))
			{
				File.Delete(Program.OutputFilePath);
			}

			using Dataset dataset = Program.driver.Create(Program.OutputFilePath, Program.cols, Program.rows, 1, DataType.GDT_Int32,
				Program.OutputOptions);
			dataset.SetProjection(Program.projection);
			Band band = dataset.GetRasterBand(1);

			if (Program.hasNoDataValue)
			{
				band.SetNoDataValue(Program.noDataValue);
			}

			band.WriteRaster(0, 0, Program.cols, Program.rows, raster, Program.cols, Program.rows, 0, 0);

			if (File.Exists($@"{Program.InputDirectory}/{Program.InputFileName}.tfw"))
			{
				File.Copy($@"{Program.InputDirectory}/{Program.InputFileName}.tfw",
					$@"{Program.OutputDirectory}/{Program.OutputFileName}.tfw", true);
			}
		}
	}
}