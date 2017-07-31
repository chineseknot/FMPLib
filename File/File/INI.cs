using System.Runtime.InteropServices;
using System.Text;

namespace FMP.MyFile
{
    public class INI
    {
        #region--------------------------Private Methods-------------------------------
        /// <summary>
        /// 读INI文件的Win API
        /// </summary>
        /// <param name="section">要读取的段落名</param>
        /// <param name="key">要读取的键</param>
        /// <param name="defVal">读取异常的情况下的缺省值</param>
        /// <param name="retVal">key所对应的值，如果该key不存在则返回空值</param>
        /// <param name="size">值允许的大小</param>
        /// <param name="filePath">INI文件的完整路径和文件名</param>
        /// <returns></returns>
        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string defVal, StringBuilder retVal, int size, string filePath);

        /// <summary>
        ///  写INI文件的Win API
        /// </summary>
        /// <param name="section">要写入的段落名</param>
        /// <param name="key">要写入的键，如果该key存在则覆盖写入</param>
        /// <param name="val">key所对应的值</param>
        /// <param name="filePath">INI文件的完整路径和文件名</param>
        /// <returns></returns>
        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        #endregion

        #region-----------------------------Public Methods-----------------------------
        /// <summary>
        /// 写入键值
        /// </summary>
        /// <param name="section">要写入的段落名</param>
        /// <param name="key">要写入的键，如果该key存在则覆盖写入</param>
        /// <param name="value">key所对应的值</param>
        /// <param name="filePath">INI文件的完整路径和文件名</param>
        public static void WriteKey(string section,string key,string value,string filePath)
        {
            WritePrivateProfileString(section, key, value, filePath);
        }

        /// <summary>
        /// 读取键值
        /// </summary>
        /// <param name="section">要读取的段落名</param>
        /// <param name="key">要读取的键</param>
        /// <param name="filePath">INI文件的完整路径和文件名</param>
        /// <param name="maxLength">读取的键值的长度上限</param>
        /// <returns>指定段落下键的值，null表示</returns>
        public static string ReadKey(string section, string key, string filePath,int maxLength = 1024)
        {
            StringBuilder retVal = new StringBuilder();
            var retCnt = GetPrivateProfileString(section, key, null, retVal, maxLength, filePath);
            //查找没有指定字段或键
            if(retCnt == 0)
            {
                return null;
            }
            //maxLength表示的是字节数目,retCnt是字符个数
            //maxLength长度超过键的字符串中字符个数时GetPrivateProfileString返回的值不再变化
            if (retCnt == GetPrivateProfileString(section, key, null, retVal, maxLength+2, filePath))
            {
                return retVal.ToString();
            }
            else
            {
                throw new AppException(AppExceptionPublic.File_INI_OutLimitLength, "ReadKey error:maxLength 设置小了");
            }
        }
        #endregion
    }
}
