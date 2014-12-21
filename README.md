
Publications:
=========================

A Training-by-Example Approach for Symbol Spotting from Raster Maps Chiang, Y.; Chioh, P.; and Moghaddam, S. In Proceedings of the 8th Geographic Information Science, 2014. 


Setup:

	1. Make sure to include EmguCV libraries in your references.
	2. Create a folder for your image set (e.g. "set01")
	3. Create folders named "in", "out", "captured" under "set01/"
	4. Put your input images in "set01/in/".
	5. Rename your input map image as "test", and the user selected patch as "element"
	6. These file name settings can be set in Main.Main()

Output:

	1. The output image will be saved as "set01/out.jpg"
	2. The folder "set01/out/" includes all the sub-images the sliding window visited.
	3. The folder "set01/captured/" is a subset of "set01/out/" that only includes the
	   sub-images with positive results (found the user selected patch).
