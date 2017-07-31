using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMP.MyFile;

namespace FMP.File.Tests
{
    [TestClass()]
    public class INITests
    {
        [TestMethod()]
        public void WriteKeyTest()
        {
            INI.WriteKey("医院", "救护车", "122432fas",@"./ test.ini");
            INI.WriteKey("医院", "心电监护仪", "18", @"./ test.ini");
            INI.WriteKey("学校", "类别", "初级中学", @"./ test.ini");
        }

        [TestMethod()]
        public void ReadKeyTest()
        {
            var ret = INI.ReadKey("医院", "救护车", @"./ test.ini");
            Console.Write(ret);
        }
    }
}