/*******************************************************************************
 * Copyright 2010 University of Southern California
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 * 	http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 * This code was developed as part of the Strabo map processing project 
 * by the Spatial Sciences Institute and by the Information Integration Group 
 * at the Information Sciences Institute of the University of Southern 
 * California. For more information, publications, and related projects, 
 * please see: http://spatial-computing.github.io/
 ******************************************************************************/

using System;
using System.IO;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.Structure;
using Emgu.CV.Util;
//using Emgu.CV.GPU;
using Emgu.Util;

namespace Strabo.Core.SymbolRecognition
{
	public class DrawMatches
	{
		public Tuple<Image<Bgr, byte>, HomographyMatrix> DrawHomography(Image<Gray, byte> model, Image<Gray, byte> observed, double uniquenessThreshold) 
        {
			HomographyMatrix homography = null;
			Image<Bgr, Byte> result = observed.Convert<Bgr, byte>();

			SURFDetector surfCPU = new SURFDetector(500, false);
			VectorOfKeyPoint modelKeyPoints;
			VectorOfKeyPoint observedKeyPoints;
			Matrix<int> indices;
			Matrix<byte> mask;
			int k = 2;

			modelKeyPoints = surfCPU.DetectKeyPointsRaw(model, null); // Extract features from the object image
			Matrix<float> modelDescriptors = surfCPU.ComputeDescriptorsRaw(model, null, modelKeyPoints);

			observedKeyPoints = surfCPU.DetectKeyPointsRaw(observed, null); // Extract features from the observed image

			if (modelKeyPoints.Size <= 0) {
				throw new System.ArgumentException("Can't find any keypoints in your model image!");
			}

			if (observedKeyPoints.Size > 0) {
				Matrix<float> observedDescriptors = surfCPU.ComputeDescriptorsRaw (observed, null, observedKeyPoints);
				BruteForceMatcher<float> matcher = new BruteForceMatcher<float> (DistanceType.L2);
				matcher.Add (modelDescriptors);

				indices = new Matrix<int> (observedDescriptors.Rows, k);

				using (Matrix<float> dist = new Matrix<float>(observedDescriptors.Rows, k)) {
					matcher.KnnMatch (observedDescriptors, indices, dist, k, null);
					mask = new Matrix<byte> (dist.Rows, 1);
					mask.SetValue (255);
					Features2DToolbox.VoteForUniqueness (dist, uniquenessThreshold, mask);
				}

				int nonZeroCount = CvInvoke.cvCountNonZero (mask);
				if (nonZeroCount >= 10) {
					nonZeroCount = Features2DToolbox.VoteForSizeAndOrientation (modelKeyPoints, observedKeyPoints, indices, mask, 1.5, 20);
					if (nonZeroCount >= 10)
						homography = Features2DToolbox.GetHomographyMatrixFromMatchedFeatures (modelKeyPoints, observedKeyPoints, indices, mask, 2);
				}

				result = Features2DToolbox.DrawMatches(model, modelKeyPoints, observed, observedKeyPoints,
					indices, new Bgr(255, 255, 255), new Bgr(255, 255, 255), mask, Features2DToolbox.KeypointDrawType.DEFAULT);
			}

			return new Tuple<Image<Bgr, byte>, HomographyMatrix>(result, homography);
		}

		public Tuple<Image<Bgr, Byte>, float[]> Draw(Image<Gray, byte> modelImage, Image<Gray, byte> observedImage, Image<Bgr, byte> modifiedImage, int x0, int y0, string path, string topic, int counter, TextWriter log)
		{
			Rectangle recShift = new Rectangle(0, 0, 0, 0);
			double bpScore = -1;
			Stopwatch watch = Stopwatch.StartNew();
			Image<Bgr, Byte> result = observedImage.Convert<Bgr, byte>();
			TextWriter capturedLog = File.AppendText(path + topic + "/capturedLog.txt");

			Tuple<Image<Bgr, byte>, HomographyMatrix> homo1 = DrawHomography(modelImage, observedImage, 1);

			if (homo1.Item2 != null)
			{
				Bitmap bD = homo1.Item1.ToBitmap();
				using (Graphics gD = Graphics.FromImage(bD))            
				{
					(observedImage.Sobel(1, 0, 3).AbsDiff(new Gray(0))+observedImage.Sobel(0, 1, 3).AbsDiff(new Gray(0))).Save(string.Format("{0}{1}/captured/oedge{2}.jpg", path, topic, counter.ToString("D5")));

					Tuple<Image<Bgr, byte>, Rectangle, double> tup = btnCalcBackProjectPatch(modelImage, observedImage, homo1.Item1);
					Bitmap bS = tup.Item1.ToBitmap();
					Rectangle rec = tup.Item2;

					gD.DrawImage(bS, new Rectangle(observedImage.Width+1, modelImage.Height+1, modelImage.Width, modelImage.Height), new Rectangle(0, 0, modelImage.Width, modelImage.Height), GraphicsUnit.Pixel);

					result = new Image<Bgr, byte>(bD);

					capturedLog.Write("\n#" + counter.ToString("D5"));

					bpScore = tup.Item3;

					if (bpScore > 0.7) {

						result.Draw(rec, new Bgr(Color.Red), 2);
						recShift = new Rectangle(new Point(x0 + rec.X, y0 + rec.Y), rec.Size);

						capturedLog.Write("\t" + tup.Item3.ToString());
					}

					result.Save(string.Format("{0}{1}/captured/{2}.jpg", path, topic, counter.ToString("D5")));
				}
			}

			result.Save(string.Format("{0}{1}/out/{2}.jpg", path, topic, counter.ToString("D5")));
			capturedLog.Close();

			watch.Stop();
			Console.WriteLine(watch.Elapsed);

			if (recShift.Width > 0 && bpScore > 0) {
				return new Tuple<Image<Bgr, byte>, float[]> (modifiedImage, new float[] {recShift.X, recShift.Y, (float)bpScore});
			} else {
				return new Tuple<Image<Bgr, byte>, float[]> (modifiedImage, new float[] {0, 0, 0});
			}
		}

		private Tuple<Image<Bgr, byte>, Rectangle, double> btnCalcBackProjectPatch(Image<Gray, byte> model, Image<Gray, byte> observed, Image<Bgr, byte> result)
		{
			Stopwatch sw = new Stopwatch();
			sw.Start();
			Image<Bgr, Byte> imageSource = observed.Convert<Bgr, byte>();
			double scale = GetScale();
			Image<Bgr, Byte> imageSource2 = imageSource.Resize(scale, INTER.CV_INTER_LINEAR);
			Image<Bgr, Byte> imageTarget = model.Convert<Bgr, byte>();
			Image<Bgr, Byte> imageTarget2 = imageTarget.Resize(scale, INTER.CV_INTER_LINEAR);
			Image<Gray, Single> imageDest = null;
			int edgeX = imageTarget2.Size.Width;
			int edgeY = imageTarget2.Size.Height;
			Size patchSize = new Size(edgeX, edgeY);
			string colorSpace = "Gray";
			double normalizeFactor = 1d;
			Image<Gray, Byte>[] imagesSource;
			int[] bins;
			RangeF[] ranges;
			string[] captions;
			Color[] colors;

			// Get all the color bins from the source images.
			GetImagesBinsAndRanges(imageSource2, colorSpace, out imagesSource, out bins, out ranges, out captions, out colors);
			// Histogram calculation.
			DenseHistogram histTarget = CalcHist(imageTarget2.Bitmap);
			// Backproject compare method.
			HISTOGRAM_COMP_METHOD compareMethod = GetHistogramCompareMethod();
			imageDest = BackProjectPatch<Byte>(imagesSource, patchSize, histTarget, compareMethod, (float)normalizeFactor);
			// Best-match value.
			double bestValue;
			Point bestPoint;
			FindBestMatchPointAndValue(imageDest, compareMethod, out bestValue, out bestPoint);
			// Draw a rectangle with the same size of the template at the best-match point

			Rectangle rect = new Rectangle(bestPoint, patchSize);
			result = imageDest.Convert<Bgr, byte>();
			sw.Stop();

			// Disposal
			imageSource.Dispose();
			imageSource2.Dispose();
			imageTarget.Dispose();
			imageTarget2.Dispose();
			if (imageDest != null)
				imageDest.Dispose();
			foreach (Image<Gray, Byte> image in imagesSource)
				image.Dispose();
			histTarget.Dispose();

			return new Tuple<Image<Bgr, byte>, Rectangle, double>(result, rect, bestValue);
		}

		// Histogram calculation.
		private DenseHistogram CalcHist(Image image)
		{
			Image<Bgr, Byte> imageSource = new Image<Bgr, byte>((Bitmap)image);
			string colorSpace = "Gray";
			Image<Gray, Byte>[] images;
			int[] bins;
			RangeF[] ranges;
			string[] captions;
			Color[] colors;
			// Get all the color bins from the source images.
			GetImagesBinsAndRanges(imageSource, colorSpace, out images, out bins, out ranges, out captions, out colors);
			SetBins(ref bins);
			// Histogram calculation.
			DenseHistogram hist = new DenseHistogram(bins, ranges);
			hist.Calculate<Byte>(images, false, null);
			return hist;
		}

		// Histogram compare method.
		private HISTOGRAM_COMP_METHOD GetHistogramCompareMethod()
		{
			HISTOGRAM_COMP_METHOD compareMethod = HISTOGRAM_COMP_METHOD.CV_COMP_CORREL;
			compareMethod = HISTOGRAM_COMP_METHOD.CV_COMP_CORREL;

			return compareMethod;
		}

		// Compares histogram over each possible rectangular patch of the specified size in the input images, and stores the results to the output map dst.
		// Destination back projection image of the same type as the source images
		public Image<Gray, Single> BackProjectPatch<TDepth>(Image<Gray, TDepth>[] srcs, Size patchSize, DenseHistogram hist, HISTOGRAM_COMP_METHOD method, double factor) where TDepth : new()
		{
			Debug.Assert(srcs.Length == hist.Dimension, "The number of the source image and the dimension of the histogram must be the same.");

			IntPtr[] imgPtrs =
				Array.ConvertAll<Image<Gray, TDepth>, IntPtr>(
					srcs,
					delegate(Image<Gray, TDepth> img) { return img.Ptr; });
			Size size = srcs[0].Size;
			size.Width = size.Width - patchSize.Width + 1;
			size.Height = size.Height - patchSize.Height + 1;
			Image<Gray, Single> res = new Image<Gray, float>(size);
			CvInvoke.cvCalcBackProjectPatch(imgPtrs, res.Ptr, patchSize, hist.Ptr, method, factor);
			return res;
		}

		public void GetImagesBinsAndRanges(Image<Bgr, Byte> imageSource, string colorSpace, out Image<Gray, Byte>[] images, out int[] bins, out RangeF[] ranges,out string[] captions,out Color[] colors)
		{
			images = null;
			bins = null;
			ranges = null;
			captions = null;
			colors = null;
			int channels = 0;
			Image<Gray, Byte> imageGray = null;
			Image<Gray, Byte> imageRed = null;
			Image<Gray, Byte> imageGreen = null;
			Image<Gray, Byte> imageBlue = null;
			Image<Gray, Byte> imageHue = null;
			Image<Gray, Byte> imageSaturation = null;
			Image<Gray, Byte> imageBrightness = null;
			if (colorSpace == "Gray")
			{
				channels = 1;
				imageGray = imageSource.Convert<Gray, Byte>();
			}
			else if (colorSpace == "Red")
			{
				channels = 1;
				Image<Gray, Byte>[] images2 = imageSource.Split();
				imageRed = images2[2];
			}
			else if (colorSpace == "RGB")
			{
				channels = 3;
				Image<Gray, Byte>[] images2 = imageSource.Split();
				imageRed = images2[2];
				imageGreen = images2[1];
				imageBlue = images2[0];
			}
			else if (colorSpace == "Hue")
			{
				channels = 1;
				Image<Hsv, Byte> imageHsv = imageSource.Convert<Hsv, Byte>();
				Image<Gray, Byte>[] images2 = imageHsv.Split();
				imageHue = images2[0];
			}
			else if (colorSpace == "H&S")
			{
				channels = 2;
				Image<Hsv, Byte> imageHsv = imageSource.Convert<Hsv, Byte>();
				Image<Gray, Byte>[] images2 = imageHsv.Split();
				imageHue = images2[0];
				imageSaturation = images2[1];
			}
			else if (colorSpace == "HSV")
			{
				channels = 3;
				Image<Hsv, Byte> imageHsv = imageSource.Convert<Hsv, Byte>();
				Image<Gray, Byte>[] images2 = imageHsv.Split();
				imageHue = images2[0];
				imageSaturation = images2[1];
				imageBrightness = images2[2];
			}
			if (channels > 0)
			{
				images = new Image<Gray, byte>[channels];
				bins = new int[channels];
				ranges = new RangeF[channels];
				captions = new string[channels];
				colors = new Color[channels];
				int idx = 0;
				if (imageGray != null)
				{
					images[idx] = imageGray;
					bins[idx] = 256;
					ranges[idx] = new RangeF(0f, 255f);
					captions[idx] = "Gray";
					colors[idx] = Color.Black;
					idx++;
				}
				if (imageRed != null)
				{
					images[idx] = imageRed;
					bins[idx] = 256;
					ranges[idx] = new RangeF(0f, 255f);
					captions[idx] = "Red";
					colors[idx] = Color.Red;
					idx++;
				}
				if (imageGreen != null)
				{
					images[idx] = imageGreen;
					bins[idx] = 256;
					ranges[idx] = new RangeF(0f, 255f);
					captions[idx] = "Green";
					colors[idx] = Color.Green;
					idx++;
				}
				if (imageBlue != null)
				{
					images[idx] = imageBlue;
					bins[idx] = 256;
					ranges[idx] = new RangeF(0f, 255f);
					captions[idx] = "Blue";
					colors[idx] = Color.Blue;
					idx++;
				}
				if (imageHue != null)
				{
					images[idx] = imageHue;
					bins[idx] = 180;
					ranges[idx] = new RangeF(0f, 179f);
					captions[idx] = "Hue";
					colors[idx] = Color.BurlyWood;
					idx++;
				}
				if (imageSaturation != null)
				{
					images[idx] = imageSaturation;
					bins[idx] = 256;
					ranges[idx] = new RangeF(0f, 255f);
					captions[idx] = "Saturation";
					colors[idx] = Color.Chocolate;
					idx++;
				}
				if (imageBrightness != null)
				{
					images[idx] = imageBrightness;
					bins[idx] = 256;
					ranges[idx] = new RangeF(0f, 255f);
					captions[idx] = "Brightness";
					colors[idx] = Color.Crimson;
					idx++;
				}
			}
		}

		private void SetBins(ref int[] bins)
		{
			int bin = 20;
			int.TryParse("25", out bin);
			for (int i = 0; i < bins.Length; i++)
				bins[i] = bin;
		}

		// Get the scaling factor.
		private double GetScale()
		{
			double scale = 1d;
			double.TryParse("1", out scale);
			return scale;
		}

		// Find the best match point and its value.
		private void FindBestMatchPointAndValue(Image<Gray, Single> image, HISTOGRAM_COMP_METHOD compareMethod, out double bestValue, out Point bestPoint)
		{
			bestValue = 0d;
			bestPoint = new Point(0, 0);
			double[] minValues, maxValues;
			Point[] minLocations, maxLocations;
			image.MinMax(out minValues, out maxValues, out minLocations, out maxLocations);
			if (compareMethod == HISTOGRAM_COMP_METHOD.CV_COMP_CHISQR || compareMethod == HISTOGRAM_COMP_METHOD.CV_COMP_BHATTACHARYYA)
			{
				bestValue = minValues[0];
				bestPoint = minLocations[0];
			}
			else
			{
				bestValue = maxValues[0];
				bestPoint = maxLocations[0];
			}
		}
	}
}

