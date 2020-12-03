// <copyright file="Program.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace RasterCostDistance
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using MaxRev.Gdal.Core;
	using OSGeo.GDAL;

	public sealed class Program
	{
		private static string Extension => "tif";

		private static string InputDirectory { get; } = @"data";

		private static string InputFileName { get; } = "settlements_clipped";

		private static string InputFilePath { get; } = @$"{Program.InputDirectory}/{Program.InputFileName}.{Program.Extension}";

		private static int Maximum => 250;

		private static string OutputDirectory { get; } = @"data/results";

		private static string OutputFileName { get; } = "settlements_clipped_250max";

		private static string OutputFilePath { get; } = @$"{Program.OutputDirectory}/{Program.OutputFileName}.{Program.Extension}";

		private static int Cols { get; set; }

		private static Driver Driver { get; set; }

		private static string Projection { get; set; }

		private static int Rows { get; set; }

		public static void Main()
		{
			GdalBase.ConfigureAll();
			Stopwatch stopwatch = Stopwatch.StartNew();

			List<int> raster = Program.LoadRaster();

			if (raster is null)
			{
				Console.WriteLine("Raster not found or unreadable. Exiting...");
				return;
			}

			Console.WriteLine(stopwatch.Elapsed + " -> Raster loaded");

			int changes;
			int iteration = 1;

			do
			{
				changes = Program.FindNeighbors(raster, iteration);
				Console.WriteLine(stopwatch.Elapsed + $"-> Iteration {iteration}: {changes} changes.");

				iteration++;
			}
			while (changes > 0 && iteration < 5);

			Program.WriteRaster(raster);

			stopwatch.Stop();
			Console.WriteLine(stopwatch.Elapsed + " -> Raster saved");
		}

		private static int FindNeighbors(List<int> raster, int currentMax)
		{
			int changes = 0;

			int newValue = currentMax + 1;

			if (newValue > Program.Maximum)
			{
				newValue = Program.Maximum;
			}

			for (int i = 0; i < raster.Count; i++)
			{
				if (raster[i] == currentMax)
				{
					changes += Program.UpdateNeighbors(raster, i, newValue);
				}
			}

			return changes;
		}

		private static (int x, int y) IndexToRowsCols(int i) => (i % Program.Cols, i / Program.Cols);

		private static List<int> LoadRaster()
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
				Program.Driver = dataset.GetDriver();
				Program.Projection = dataset.GetProjection();
				Program.Cols = band.XSize;
				Program.Rows = band.YSize;

				int[] buffer = new int[band.XSize * band.YSize];
				band.ReadRaster(0, 0, band.XSize, band.YSize, buffer, band.XSize, band.YSize, 0, 0);

				return buffer.ToList();
			}
#pragma warning disable CA1031 // Do not catch general exception types
			catch (Exception e)
			{
				Console.WriteLine(e);
				return null;
			}
#pragma warning restore CA1031 // Do not catch general exception types
		}

		private static int RowsColsToIndex(int x, int y) => x + (y * Program.Cols);

		// Update the neighbor if it still has its default setting (0)
		private static int UpdateNeighbor(List<int> raster, int x, int y, int newValue)
		{
			int i = Program.RowsColsToIndex(x, y);

			if (raster[i] == 0)
			{
				raster[i] = newValue;
				return 1;
			}

			return 0;
		}

		// Updating N8 neighbors
		private static int UpdateNeighbors(List<int> raster, int i, int newValue)
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

			if (x > 0 && y < Program.Rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x - 1, y + 1, newValue);
			}

			if (y > 0)
			{
				changes += Program.UpdateNeighbor(raster, x, y - 1, newValue);
			}

			if (y < Program.Rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x, y + 1, newValue);
			}

			if (x < Program.Cols - 1 && y > 0)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y - 1, newValue);
			}

			if (x < Program.Cols - 1)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y, newValue);
			}

			if (x < Program.Cols - 1 && y < Program.Rows - 1)
			{
				changes += Program.UpdateNeighbor(raster, x + 1, y + 1, newValue);
			}

			return changes;
		}

		private static void WriteRaster(List<int> raster)
		{
			if (!Directory.Exists(Program.OutputDirectory))
			{
				Directory.CreateDirectory(Program.OutputDirectory);
			}

			if (File.Exists(Program.OutputFilePath))
			{
				File.Delete(Program.OutputFilePath);
			}

			using Dataset dataset = Program.Driver.Create(Program.OutputFilePath, Program.Cols, Program.Rows, 1, DataType.GDT_Int32,
				new[] { "TFW=YES", "COMPRESS=DEFLATE" });
			dataset.SetProjection(Program.Projection);
			Band band = dataset.GetRasterBand(1);
			band.WriteRaster(0, 0, Program.Cols, Program.Rows, raster.ToArray(), Program.Cols, Program.Rows, 0, 0);

			if (File.Exists($@"{Program.InputDirectory}/{Program.InputFileName}.tfw"))
			{
				File.Copy($@"{Program.InputDirectory}/{Program.InputFileName}.tfw",
					$@"{Program.OutputDirectory}/{Program.OutputFileName}.tfw", true);
			}
		}
	}
}