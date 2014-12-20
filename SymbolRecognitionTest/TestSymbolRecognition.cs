using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Strabo.Core.SymbolRecognition;

namespace SymbolRecognitionTest
{
    public class TestSymbolRecognition
    {
        public static void Test()
        {
            SymbolRecognitionWorker srw = new SymbolRecognitionWorker();
            srw.Apply(@"../../data/test_maps/","Hollywood");
        }
    }
}
