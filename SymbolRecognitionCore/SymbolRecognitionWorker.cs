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

using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Strabo.Core.SymbolRecognition
{
    public class SymbolRecognitionWorker
    {
        // The result consolidation function.
        public HashSet<float[]> consolidate(ArrayList al, int w, int h, TextWriter log)
        {
            ArrayList al2 = new ArrayList();

            foreach (float[] i in al)
            {
                al2.Add(i);
            }

            foreach (float[] i in al)
            {
                foreach (float[] j in al)
                {
                    if (!((Math.Abs(i[0] - j[0]) > w) ||
                        (Math.Abs(i[1] - j[1]) > h) ||
                        (Math.Sqrt(Math.Pow(i[0] - j[0], 2) + Math.Pow(i[1] - j[1], 2)) > Math.Sqrt(Math.Pow(w, 2) + Math.Pow(h, 2)))))
                    {

                        if (i[2] > j[2])
                        {
                            al2[al.IndexOf(j)] = i;
                        }
                        else if (i[2] < j[2])
                        {
                            al2[al.IndexOf(i)] = j;
                        }
                    }
                }
            }

            HashSet<float[]> hash = new HashSet<float[]>();

            foreach (float[] i in al2)
            {
                hash.Add(i);
            }

            log.WriteLine("The count of al2: " + al2.Count);
            log.WriteLine("The count of hash: " + hash.Count);

            al.Clear();

            foreach (float[] i in hash)
            {
                al.Add(i);
            }

            log.WriteLine("The count after hash: " + al.Count);

            return hash;
        }

        public void Apply(string path, string map)
        {
            Stopwatch watch = Stopwatch.StartNew();
            ArrayList allMatches = new ArrayList();
            Tuple<Image<Bgr, byte>, float[]> drawResult;
            float[] recStat;
            // Handling file names.
            
            string topic = map;
            TextWriter log = File.AppendText(path + topic + "\\log.txt");

            // Read images.
            Image<Bgr, Byte> element = new Image<Bgr, Byte>(string.Format("{0}{1}\\in\\element.png", path, topic));
            Image<Bgr, Byte> test = new Image<Bgr, Byte>(string.Format("{0}{1}\\in\\test.png", path, topic));
            Bitmap window = test.ToBitmap();

            // Convert to gray-level images and save.
            Image<Gray, Byte> gElement = element.Convert<Gray, Byte>();
            Image<Gray, Byte> gTest = test.Convert<Gray, Byte>();
            gElement.Save(string.Format("{0}{1}\\in\\g-element.png", path, topic));
            gTest.Save(string.Format("{0}{1}\\in\\g-test.png", path, topic));

            // Get image dimensions.
            int wfactor = 2;
            // The size of the element image.
            int ex = element.Width;
            int ey = element.Height;
            // The size of the test image.
            int tx = test.Width;
            int ty = test.Height;
            // The distance that the sliding window shifts.
            int xshift = tx / ex / wfactor * 2 - 1;
            int yshift = ty / ey / wfactor * 2 - 1;

            log.WriteLine(string.Format("Element Image: ({0}*{1})\nTest Image:({2}*{3})\n", ex, ey, tx, ty));

            for (int j = 0; j < yshift; j++)
            {
                for (int i = 0; i < xshift; i++)
                {
                    int xstart = i * ex * wfactor / 2;
                    int ystart = j * ey * wfactor / 2;
                    int counter = i + j * xshift;

                    Rectangle r = new Rectangle(xstart, ystart, ex * wfactor, ey * wfactor);
                    Image<Bgr, Byte> pTest = new Image<Bgr, Byte>(window.Clone(r, window.PixelFormat));
                    pTest.Save(string.Format("{0}{1}\\in\\part.jpg", path, topic));

                    DrawMatches dm = new DrawMatches();

                    drawResult = dm.Draw(gElement, pTest.Convert<Gray, Byte>(), test, xstart, ystart, path, topic, counter, log);

                    log.WriteLine(string.Format("\n\nSub-image #{0}:\n\tLoop #({1}, {2})\n\tSW1 location: ({3}, {4})", counter, i, j, xstart, ystart));
                    test = drawResult.Item1;
                    recStat = drawResult.Item2;
                    if (recStat[2] > 0)
                    {
                        allMatches.Add(recStat);
                        log.WriteLine(string.Format("\n\tSW2 location: ({0}, {1})\n\tHistogram score: {2}]", recStat[0], recStat[1], recStat[2]));
                    }
                }
            }

            log.WriteLine("The count before consolidation: " + allMatches.Count);

            HashSet<float[]> hash0 = consolidate(allMatches, gElement.Width - 1, gElement.Height - 1, log);
            ArrayList al = new ArrayList();

            foreach (float[] i in hash0)
            {
                al.Add(i);
            }

            HashSet<float[]> hash = consolidate(al, gElement.Width - 1, gElement.Height - 1, log);

            log.WriteLine("The count after consolidation: " + hash.Count);

            foreach (float[] i in hash)
            {

                test.Draw(new Rectangle(new Point((int)i[0], (int)i[1]), gElement.Size), new Bgr(Color.Blue), 5);
            }

            test.Save(string.Format("{0}{1}\\out.jpg", path, topic));

            watch.Stop();
            log.WriteLine(watch.Elapsed);

            log.Close();
        }
    }
}
