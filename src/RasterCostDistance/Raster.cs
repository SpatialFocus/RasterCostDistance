// <copyright file="Raster.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace RasterCostDistance
{
	using System;
	using System.Diagnostics.CodeAnalysis;
	using System.IO;
	using MaxRev.Gdal.Core;
	using OSGeo.GDAL;

	public class Raster
	{
		private Driver driver;
		private bool hasNoDataValue;
		private double noDataValue;
		private string projection;

		[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Cells are updated in Main")]
		public int[] Cells { get; set; }

		public int Cols { get; private set; }

		public int Rows { get; private set; }

		public static Raster Load(string inputFilePath)
		{
			GdalBase.ConfigureAll();

			if (!File.Exists(inputFilePath))
			{
				return null;
			}

			try
			{
				using Dataset dataset = Gdal.Open(inputFilePath, Access.GA_ReadOnly);

				Band band = dataset.GetRasterBand(1);
				band.GetNoDataValue(out double noDataValue, out int hasVal);

				int[] buffer = new int[band.XSize * band.YSize];
				band.ReadRaster(0, 0, band.XSize, band.YSize, buffer, band.XSize, band.YSize, 0, 0);

				return new Raster
				{
					driver = dataset.GetDriver(),
					projection = dataset.GetProjection(),
					Cols = band.XSize,
					Rows = band.YSize,
					hasNoDataValue = hasVal == 1,
					noDataValue = noDataValue,
					Cells = buffer,
				};
			}
#pragma warning disable CA1031 // Do not catch general exception types
			catch (Exception)
			{
				return null;
			}
#pragma warning restore CA1031 // Do not catch general exception types
		}

		public (int x, int y) IndexToRowsCols(int i) => (i % Cols, i / Cols);

		public int RowsColsToIndex(int x, int y) => x + (y * Cols);

		public void Write(string outputFilePath, string[] outputOptions)
		{
			if (File.Exists(outputFilePath))
			{
				File.Delete(outputFilePath);
			}

			using Dataset dataset = this.driver.Create(outputFilePath, Cols, Rows, 1, DataType.GDT_Int32, outputOptions);
			dataset.SetProjection(this.projection);

			Band band = dataset.GetRasterBand(1);

			if (this.hasNoDataValue)
			{
				band.SetNoDataValue(this.noDataValue);
			}

			band.WriteRaster(0, 0, Cols, Rows, Cells, Cols, Rows, 0, 0);
		}
	}
}