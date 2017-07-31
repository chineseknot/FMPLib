using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FMP
{
    #region ---------------公开类定义-----------------

    /// <summary>
    /// 日志级别定义
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// DEBUG Level指出细粒度信息事件对调试应用程序是非常有帮助的
        /// </summary>
        DEBUG,

        /// <summary>
        /// INFO level表明 消息在粗粒度级别上突出强调应用程序的运行过程
        /// </summary>
        INFO,

        /// <summary>
        /// WARN level表明会出现潜在错误的情形。
        /// </summary>
        WARN,

        /// <summary>
        /// ERROR level指出虽然发生错误事件，但仍然不影响系统的继续运行。
        /// </summary>
        ERROR,

        /// <summary>
        /// FATAL level指出每个严重的错误事件将会导致应用程序的退出。
        /// </summary>
        FATAL
    }

    /// <summary>
    /// 日志打印类，用于在程序逻辑中打印一些日记记录到文件，方便调试程序，
    /// 同时客户使用时如果遇到问题，也可以使能日志打印的功能，方便定位错误的原因
    /// </summary>
    public static class Log
    {
        private static LogLevel _logLevel;
        /// <summary>
        /// 日志等级，默认为ERROR
        /// </summary>
        public static LogLevel LogLevel
        {
            get
            {
                return _logLevel;
            }
            set
            {
                _logLevel = value;
            }
        }

        /// <summary>
        /// 用于日志消息缓存的队列，首次调用时初始化
        /// </summary>
        private static Queue _logMsgQ;

        private static Task _tskWriteFile;
        private static bool _writting;

        private static bool _enableLog;
        /// <summary>
        /// 使能日志打印功能
        /// </summary>
        public static bool EnableLog
        {
            set
            {
                _enableLog = _writting = value;
                if (value)
                {
                    if (_tskWriteFile == null)
                    {
                        _tskWriteFile = new Task(FuncWriteLog, TaskCreationOptions.LongRunning);
                        _tskWriteFile.Start();
                    }
                }
                else
                {
                    if (_tskWriteFile != null && _tskWriteFile.Status == TaskStatus.Running)
                    {
                        _tskWriteFile.Wait(100);
                        _tskWriteFile.Dispose();
                    }
                    _tskWriteFile = null;
                }
            }
            get
            {
                return _enableLog;
            }
        }



        static Log()
        {
            _enableLog = false;               //初始化为不打印日志            
            Queue q = new Queue(1024);        //初始化大小为1024
            _logMsgQ = Queue.Synchronized(q); //获取Queue.Synchronized方法包装的Queue
            _logLevel = LogLevel.ERROR;
        }

        /// <summary>
        /// 写入日志文件,需要定义宏ENABLELOG 或 DEBUG，此方法兼容旧版
        /// </summary>
        /// <param name="logMsg">要打印的消息内容</param>
        /// <param name="args">参数</param>
        public static void Print(string logMsg, params object[] args)
        {
#if (ENABLELOG || DEBUG)
            if (_enableLog == false)
            {
                return;
            }
            DateTime t = DateTime.Now;
            var msg = string.Format(logMsg, args); //2016-04-26 10:59:10.6679687
            var s_t = $"[{t.Year:D4}-{t.Month:D2}-{t.Day:D2} {t.TimeOfDay.ToString()}]\t{msg}";
            _logMsgQ.Enqueue(s_t);
#endif
        }

        /// <summary>
        /// 写入日志文件,需要定义宏ENABLELOG 或 DEBUG
        /// </summary>
        /// <param name="logLevel">日志等级</param>
        /// <param name="logMsg">要打印的消息内容</param>
        /// <param name="args">参数</param>
        public static void Print(LogLevel logLevel, string logMsg, params object[] args)
        {
#if (ENABLELOG || DEBUG) //对于Release版本可以选择关闭日志功能，对于Debug版本默认开启
            if (_enableLog == false || ((int)logLevel < (int)_logLevel))
            {
                return;
            }
            DateTime t = DateTime.Now;
            var msg = string.Format(logMsg, args); //2016-04-26 10:59:10.6679687
            var s_t = $"[{t.Year:D4}-{t.Month:D2}-{t.Day:D2} {t.TimeOfDay.ToString()}]\t{msg}";
            _logMsgQ.Enqueue(s_t);
#endif
        }

        /// <summary>
        /// 轮询日志队列的定时器回调函数
        /// </summary>
        /// <param name="state"></param>
        private static void FuncWriteLog()
        {
            while (_writting)
            {
                //如果之前的回调正在进行或日志队列为空，都直接返回
                if (_logMsgQ.Count <= 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                FileInfo finfo = null;
                FileStream fs = null;
                StreamWriter w = null;
                try
                {
                    DateTime t = DateTime.Now;
                    //指定日志文件的目录
                    string fname = Directory.GetCurrentDirectory() + "\\"
                                   + t.Year.ToString("D04") + t.Month.ToString("D02") + t.Day.ToString("D02") + ".log";

                    //定义文件信息对象
                    finfo = new FileInfo(fname);
                    fs = null;
                    if (!finfo.Exists)
                    {
                        fs = File.Create(fname);
                        fs.Close();
                        finfo = new FileInfo(fname);
                    }

                    //判断文件是否存在以及是否大于10M
                    if (finfo.Length > 1024 * 1024 * 10)
                    {
                        //文件超过10MB则重命名
                        File.Move(
                            Directory.GetCurrentDirectory() + "\\" + t.Year.ToString("D04") + t.Month.ToString("D02") +
                            t.Day.ToString("D02") + ".log",
                            Directory.GetCurrentDirectory() + "\\" + t.Year.ToString("D04") + t.Month.ToString("D02") +
                            t.Day.ToString("D02") + t.Hour.ToString("D02") + t.Minute.ToString("D02") +
                            t.Second.ToString("D02") + ".log");
                    }

                    fs = finfo.OpenWrite();
                    w = new StreamWriter(fs);

                    while (_logMsgQ.Count > 0)
                    {
                        //设置写数据流的起始位置为文件流的末尾
                        w.BaseStream.Seek(0, SeekOrigin.End);
                        var s_t = _logMsgQ.Dequeue().ToString();
                        System.Diagnostics.Debug.Print(s_t);
                        //写入日志内容并换行
                        w.Write(s_t + "\r\n");
                    }
                }
                catch (Exception ex)
                {

                }
                finally
                {
                    //清空缓冲区内容，并把缓冲区内容写入基础流
                    w?.Flush();
                    //关闭写数据流
                    w?.Close();
                    fs?.Close();
                }
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>
    /// 驱动常用错误代码枚举定义
    /// </summary>
    public enum AppExceptionPublic
    {
        /// <summary>
        /// 未知错误
        /// </summary>
        UnKnown,

        /// <summary>
        /// 超出读取数据的限制长度
        /// </summary>
        File_INI_OutLimitLength,
    }

	/// <summary>
    /// 异常类
    /// </summary>
    public sealed class AppException : ApplicationException
    {
        /// <summary>
        /// 异常链表指针, 指向后一个异常。
        /// </summary>
        public AppException FollowingException { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public AppException(int errorCode) : base(@"Driver internal error, ErrorCode=" + errorCode.ToString())
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public AppException(AppExceptionPublic exceptionCode) 
            : this(exceptionCode, "Driver exception, ExceptionName=" + Enum.GetName(typeof(AppExceptionPublic), exceptionCode))
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public AppException(string msg, AppExceptionPublic exceptionCode)
            : this(exceptionCode, msg)
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="msg">异常描述信息</param>
        /// <param name="errorCode">内部错误代码，通常是底层驱动返回的原始错误代码，无则默认为0</param>
        public AppException(AppExceptionPublic exceptionCode, string msg, params object[] args) 
            :this((int)exceptionCode, msg, args)
        {
            ExceptionName = exceptionCode;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="errorCode">内部错误代码，通常是底层驱动返回的原始错误代码，无则默认为0</param>
        /// <param name="msg">异常描述信息</param>
        /// <param name="args">格式化参数</param>
        public AppException(int errorCode, string msg,  params object[] args) : this(string.Format(msg, args), null)
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="msg">异常描述信息</param>
        /// <param name="innerException">导致当前异常的异常。如果 innerException 不为 null，则在处理内部异常的 catch 块中引发当前异常。</param>
        /// <param name="errorCode">内部错误代码，通常是底层驱动返回的原始错误代码，无则默认为0</param>
        public AppException(string msg, Exception innerException) : base(msg, innerException)
        {
        }

        /// <summary>
        /// 错误代码
        /// </summary>
        public int ErrorCode = 0;

        public AppExceptionPublic ExceptionName = AppExceptionPublic.UnKnown;
    }

    #endregion

    #region ---------------内部使用类定义-----------------
    /// <summary>
    /// 线程抛出异常管理类
    /// </summary>
    public class DriverThreadExceptionManager
    {
        /// <summary>
        /// 异常链表头指针。收集非主线程产生的异常并链接成链表，在调用Stop()方法时将这些异常反馈给用户。
        /// </summary>
        private AppException _headException;

        /// <summary>
        /// 异常链表当前节点
        /// </summary>
        private AppException _currentException;

        /// <summary>
        /// 清除非主线程产生的异常
        /// </summary>
        public void ClearThreadExceptions()
        {
            _headException = null;
            _currentException = null;
        }

        /// <summary>
        /// 报告异常
        /// </summary>
        public void ReportThreadExceptions()
        {
            if (_headException != null)
            {
                AppException tempException = _headException;
                _headException = null;
                throw tempException;
            }
        }

        /// <summary>
        /// 将异常添加到异常变量
        /// </summary>
        /// <param name="e">异常对象</param>
        /// <remarks>
        /// 若使用 
        /// <code> (DaqTaskException)Activator.CreateInstance(e.GetType(), e.Message, _currentException);</code>
        /// , 虽不必使用FollowingException属性, 但 e.InnerException 信息将丢失.
        /// </remarks>
        public void AppendThreadException(AppException e)
        {
            if (_headException == null)
                _headException = e;
            else
                _currentException.FollowingException = e;
            _currentException = e;
        }
    }

    /// <summary>
    /// 循环缓冲队列类，旧版本，托管内存
    /// </summary>
    /// <typeparam name="T">泛型</typeparam>
    public class CircularBuffer<T>
    {
        private readonly int _sizeOfT; //T的Size，创建队列的时候初始化
        private T[] _buffer;           //缓冲区

        private int _WRIdx;           //队列写指针
        private int _RDIdx;           //队列读指针

        private volatile int _numOfElement;
        /// <summary>
        /// 当前的元素个数
        /// </summary>
        public int NumOfElement
        {

            get
            {
                lock (this)
                {
                    return _numOfElement;
                }
            }
        }

        private int _bufferSize;       //循环队列缓冲的大小 
        /// <summary>
        /// 缓冲区的大小
        /// </summary>
        public int BufferSize
        {
            get { return _bufferSize; }
        }

        /// <summary>
        /// 当前能容纳的点数
        /// </summary>
        public int CurrentCapacity
        {
            get
            {
                lock (this)
                {
                    return _bufferSize - _numOfElement;
                }
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="bufferSize"></param>
        public CircularBuffer(int bufferSize)
        {
            if (bufferSize <= 0) //输入的size无效，创建默认大小的缓冲区
            {
                bufferSize = 1024;
            }
            _bufferSize = bufferSize;

            _buffer = new T[_bufferSize]; //新建对应大小的缓冲区

            _WRIdx = 0;
            _RDIdx = 0;    //初始化读写指针

            _numOfElement = 0;

            _sizeOfT = Marshal.SizeOf(_buffer[0]);
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public CircularBuffer()
        {
            _bufferSize = 1024;

            _buffer = new T[_bufferSize]; //新建对应大小的缓冲区

            _WRIdx = 0;
            _RDIdx = 0;    //初始化读写指针

            _numOfElement = 0;

            _sizeOfT = Marshal.SizeOf(_buffer[0]);
        }

        /// <summary>
        /// 调整缓冲区大小，数据会被清空
        /// </summary>
        /// <param name="size"></param>
        public void AdjustSize(int size)
        {
            lock (this)
            {
                if (size <= 0)
                {
                    size = 1; //最小size应当为1
                }
                this.Clear();
                _bufferSize = size;
                _buffer = new T[_bufferSize];
            }
        }

        /// <summary>
        /// 清空缓冲区内的数据
        /// </summary>
        public void Clear()
        {
            lock (this)
            {
                _numOfElement = 0;
                _WRIdx = 0;
                _RDIdx = 0;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一个数据
        /// </summary>
        /// <param name="element"></param>
        public int Enqueue(T element)
        {
            lock (this)
            {
                if (_numOfElement >= _bufferSize)
                {
                    return -1;
                }
                _buffer[_WRIdx] = element;

                if (_WRIdx + 1 >= _bufferSize)
                {
                    _WRIdx = 0;
                }
                else
                {
                    _WRIdx++;
                }

                _numOfElement++;
                return 1;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="elements"></param>
        public int Enqueue(T[] elements)
        {
            lock (this)
            {
                if (_numOfElement + elements.Length > _bufferSize)
                {
                    Log.Print("Enqueue error");
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + elements.Length > _bufferSize)
                {
                    Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, (_bufferSize - _WRIdx) * _sizeOfT);
                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = elements.Length - PutCnt;
                    _WRIdx = 0;
                    Buffer.BlockCopy(elements, PutCnt * _sizeOfT, _buffer, _WRIdx * _sizeOfT, remainCnt * _sizeOfT);
                    _WRIdx = remainCnt;
                }
                else
                {
                    Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, elements.Length * _sizeOfT);
                    if (_WRIdx + elements.Length == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += elements.Length;
                    }
                }
                _numOfElement += elements.Length;

                return elements.Length;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="elements"></param>
        public int Enqueue(T[] elements, int len)
        {
            lock (this)
            {
                if (_numOfElement + len > _bufferSize)
                {
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + len > _bufferSize)
                {
                    Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, (_bufferSize - _WRIdx) * _sizeOfT);
                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = len - PutCnt;
                    _WRIdx = 0;
                    Buffer.BlockCopy(elements, PutCnt * _sizeOfT, _buffer, _WRIdx * _sizeOfT, remainCnt * _sizeOfT);
                    _WRIdx = remainCnt;
                }
                else
                {
                    Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, len * _sizeOfT);
                    if (_WRIdx + len == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += len;
                    }
                }
                _numOfElement += len;

                return len;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="elements"></param>
        public int Enqueue(T[,] elements)
        {
            lock (this)
            {
                if (_numOfElement + elements.Length > _bufferSize)
                {
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + elements.Length > _bufferSize)
                {
                    Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, (_bufferSize - _WRIdx) * _sizeOfT);
                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = elements.Length - PutCnt;
                    _WRIdx = 0;
                    Buffer.BlockCopy(elements, PutCnt * _sizeOfT, _buffer, _WRIdx * _sizeOfT, remainCnt * _sizeOfT);
                    _WRIdx = remainCnt;
                }
                else
                {
                    Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, elements.Length * _sizeOfT);
                    if (_WRIdx + elements.Length == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += elements.Length;
                    }
                }
                _numOfElement += elements.Length;

                return elements.Length;
            }
        }

        /// <summary>
        /// 从缓冲队列中取一个数据
        /// </summary>
        /// <returns>失败：-1，1：返回一个数据</returns>
        public int Dequeue(ref T reqElem)
        {
            lock (this)
            {
                if (_numOfElement <= 0)
                {
                    return -1;
                }
                _numOfElement--;

                reqElem = _buffer[_RDIdx];

                if (_RDIdx + 1 >= _bufferSize)
                {
                    _RDIdx = 0;
                }
                else
                {
                    _RDIdx++;
                }

                return 1;
            }
        }

        /// <summary>
        /// 从缓冲队列中取出指定长度的数据
        /// </summary>
        /// <param name="reqBuffer">请求读取缓冲区</param>
        /// <returns>返回实际取到的数据长度</returns>
        public int Dequeue(ref T[] reqBuffer, int len)
        {
            lock (this)
            {
                int getCnt = len;

                if (len > _numOfElement || _numOfElement <= 0)
                {
                    return -1;
                }
                else if (len <= 0)
                {
                    getCnt = _numOfElement;
                }

                if (_RDIdx + getCnt > _bufferSize)   //取数据的总大小超过了应该分两次拷贝，先拷贝尾部，剩余的从头开始拷贝
                {
                    Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, 0, (_bufferSize - _RDIdx) * _sizeOfT);
                    int fetchedCnt = (_bufferSize - _RDIdx);
                    int remainCnt = getCnt - fetchedCnt;
                    _RDIdx = 0;
                    Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, fetchedCnt * _sizeOfT, remainCnt * _sizeOfT);
                    _RDIdx = remainCnt;
                }
                else
                {
                    Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, 0, getCnt * _sizeOfT);
                    if (_RDIdx + getCnt == _bufferSize)
                    {
                        _RDIdx = 0;
                    }
                    else
                    {
                        _RDIdx += getCnt;
                    }
                }
                _numOfElement -= getCnt;
                return getCnt;
            }
        }

        /// <summary>
        /// 从缓冲队列中取出指定长度的数据
        /// </summary>
        /// <param name="reqBuffer">请求读取缓冲区</param>
        /// <returns>返回实际取到的数据长度</returns>
        public int Dequeue(ref T[,] reqBuffer, int len)
        {
            lock (this)
            {
                int getCnt = len;

                if (len > _numOfElement || _numOfElement <= 0)
                {
                    return -1;
                }
                else if (len <= 0)
                {
                    getCnt = _numOfElement;
                }

                if (_RDIdx + getCnt > _bufferSize)   //取数据的总大小超过了应该分两次拷贝，先拷贝尾部，剩余的从头开始拷贝
                {
                    Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, 0, (_bufferSize - _RDIdx) * _sizeOfT);
                    int fetchedCnt = (_bufferSize - _RDIdx);
                    int remainCnt = getCnt - fetchedCnt;
                    _RDIdx = 0;
                    Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, fetchedCnt * _sizeOfT, remainCnt * _sizeOfT);
                    _RDIdx = remainCnt;
                }
                else
                {
                    Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, 0, getCnt * _sizeOfT);
                    if (_RDIdx + getCnt == _bufferSize)
                    {
                        _RDIdx = 0;
                    }
                    else
                    {
                        _RDIdx += getCnt;
                    }
                }
                _numOfElement -= getCnt;
                return getCnt;
            }
        }
    }

    /// <summary>
    /// windows api 的函数导入
    /// </summary>
    public static class WinAPI
    {
        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern void memcpy(IntPtr destination, IntPtr source, UIntPtr length);
    }

    /// <summary>
    /// 循环缓冲队列扩展类（非托管内存），主要用于实现循环缓冲链，该类较高，推荐使用，但跨平台需要重新修改
    /// </summary>
    /// <typeparam name="T">泛型</typeparam>
    public class CircularBufferEx<T>
    {
        /// <summary>
        /// T的Size，创建队列的时候初始化
        /// </summary>
        private readonly int _sizeOfT; 

        /// <summary>
        /// 缓冲区的指针首地址
        /// </summary>
        private IntPtr _bufferPtr;

        /// <summary>
        /// 队列写指针
        /// </summary>
        private int _WRIdx;           

        /// <summary>
        /// 队列读指针
        /// </summary>
        private int _RDIdx;          

        private volatile int _numOfElement;
        /// <summary>
        /// 当前的元素个数
        /// </summary>
        public int NumOfElement
        {
            get
            {
                lock (this)
                {
                    return _numOfElement;
                }
            }
        }

        private int _bufferSize; //循环队列缓冲的大小 
        /// <summary>
        /// 缓冲区的大小
        /// </summary>
        public int BufferSize
        {
            get { return _bufferSize; }
        }

        /// <summary>
        /// 当前能容纳的点数
        /// </summary>
        public int CurrentCapacity
        {
            get
            {
                lock (this)
                {
                    return _bufferSize - _numOfElement;
                }
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="bufferSize"></param>
        public CircularBufferEx(int bufferSize)
        {
            if (bufferSize <= 0) //输入的size无效，创建默认大小的缓冲区
            {
                bufferSize = 1024;
            }
            _bufferSize = bufferSize;
            _sizeOfT = Marshal.SizeOf(typeof(T));
            _bufferPtr = Marshal.AllocHGlobal(bufferSize * _sizeOfT); //新建对应大小的缓冲区

            _WRIdx = 0;
            _RDIdx = 0;    //初始化读写指针

            _numOfElement = 0;            
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public CircularBufferEx()
        {
            _bufferSize = 1024;

            _sizeOfT = Marshal.SizeOf(Marshal.SizeOf(typeof(T)));
            _bufferPtr = Marshal.AllocHGlobal(_bufferSize * _sizeOfT); //新建对应大小的缓冲区

            _WRIdx = 0;
            _RDIdx = 0;    //初始化读写指针

            _numOfElement = 0;
        }

        /// <summary>
        /// 释放循环缓冲区
        /// </summary>
        public void Dispose()
        {
            lock (this)
            {
                if (!_bufferPtr.Equals(IntPtr.Zero))
                {
                    Marshal.FreeHGlobal(_bufferPtr);
                }
                _bufferPtr = IntPtr.Zero;
                _numOfElement = 0;
                _WRIdx = 0;
                _RDIdx = 0;
            }
        }

        /// <summary>
        /// 清空循环缓冲区的数据
        /// </summary>
        public void Clear()
        {
            lock (this)
            {
                if (!_bufferPtr.Equals(IntPtr.Zero))
                {
                    _numOfElement = 0;
                    _WRIdx = 0;
                    _RDIdx = 0;
                }
            }
        }

        /// <summary>
        /// 析构函数，避免非托管内存没有释放
        /// </summary>
        ~CircularBufferEx()
        {
            Dispose();
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="elements"></param>
        public int Enqueue(T[] elements)
        {
            lock (this)
            {
                if (_numOfElement + elements.Length > _bufferSize)
                {
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + elements.Length > _bufferSize)
                {
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _WRIdx) * _sizeOfT));

                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = elements.Length - PutCnt;
                    _WRIdx = 0;

                    dstPtr = _bufferPtr;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + PutCnt * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + PutCnt * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));

                    gch.Free();

                    _WRIdx = remainCnt;
                }
                else
                {
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }

                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(elements.Length * _sizeOfT));
                    gch.Free();

                    if (_WRIdx + elements.Length == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += elements.Length;
                    }
                }
                _numOfElement += elements.Length;

                return elements.Length;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="elements"></param>
        public int Enqueue(T[] elements, int len)
        {
            lock (this)
            {
                if (_numOfElement + len > _bufferSize)
                {
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + len > _bufferSize)
                {
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }

                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _WRIdx) * _sizeOfT));

                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = elements.Length - PutCnt;
                    _WRIdx = 0;
                    dstPtr = _bufferPtr;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + PutCnt * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + PutCnt * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));

                    gch.Free();

                    _WRIdx = remainCnt;
                }
                else
                {
                    //Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, len * _sizeOfT);
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(elements.Length * _sizeOfT));
                    gch.Free();

                    if (_WRIdx + len == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += len;
                    }
                }
                _numOfElement += len;

                return len;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="elements"></param>
        public int Enqueue(T[] elements, int srcIdx, int len)
        {
            lock (this)
            {
                if (_numOfElement + len > _bufferSize)
                {
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + len > _bufferSize)
                {
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + srcIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + srcIdx * _sizeOfT);
                    }
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _WRIdx) * _sizeOfT));

                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = len - PutCnt;
                    _WRIdx = 0;
                    dstPtr = _bufferPtr;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + (PutCnt + srcIdx) * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + (PutCnt + srcIdx) * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));

                    gch.Free();

                    _WRIdx = remainCnt;
                }
                else
                {
                    //Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, len * _sizeOfT);
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + srcIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + srcIdx * _sizeOfT);
                    }

                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(len * _sizeOfT));
                    gch.Free();

                    if (_WRIdx + len == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += len;
                    }
                }
                _numOfElement += len;

                return len;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="elements"></param>
        public int Enqueue(T[,] elements)
        {
            lock (this)
            {
                if (_numOfElement + elements.Length > _bufferSize)
                {
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + elements.Length > _bufferSize)
                {
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _WRIdx) * _sizeOfT));

                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = elements.Length - PutCnt;
                    _WRIdx = 0;
                    dstPtr = _bufferPtr;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + PutCnt * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + PutCnt * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));

                    gch.Free();

                    _WRIdx = remainCnt;
                }
                else
                {
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(elements.Length * _sizeOfT));
                    gch.Free();
                    if (_WRIdx + elements.Length == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += elements.Length;
                    }
                }
                _numOfElement += elements.Length;

                return elements.Length;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="elements"></param>
        public int Enqueue(T[,] elements, int srcIdx, int len)
        {
            lock (this)
            {
                if (_numOfElement + len > _bufferSize)
                {
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + len > _bufferSize)
                {
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + srcIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + srcIdx * _sizeOfT);
                    }

                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _WRIdx) * _sizeOfT));

                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = len - PutCnt;
                    _WRIdx = 0;
                    dstPtr = _bufferPtr;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + (PutCnt + srcIdx) * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + (PutCnt + srcIdx) * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));

                    gch.Free();

                    _WRIdx = remainCnt;
                }
                else
                {
                    var gch = GCHandle.Alloc(elements, GCHandleType.Pinned);
                    var srcPtr = gch.AddrOfPinnedObject();
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + srcIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + srcIdx * _sizeOfT);
                    }
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(len * _sizeOfT));
                    gch.Free();

                    if (_WRIdx + len == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += len;
                    }
                }
                _numOfElement += len;

                return len;
            }
        }

        /// <summary>
        /// 向缓冲队列中放入一组数据
        /// </summary>
        /// <param name="dataPtr">数据首地址</param>
        /// <param name="srcIdx">数据开始的索引（以元素为单位，非字节单位）</param>
        /// <param name="len">数据长度（以元素为单位，非字节单位）</param>
        /// <returns></returns>
        public int Enqueue(IntPtr dataPtr, int srcIdx, int len)
        {
            lock (this)
            {
                if (_numOfElement + len > _bufferSize)
                {
                    return -1;
                }

                //超出数组尾部了，应该分两次拷贝进去，先拷贝_WRIdx到结尾的，再从头开始拷贝
                if (_WRIdx + len > _bufferSize)
                {
                    var srcPtr = dataPtr;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + srcIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + srcIdx * _sizeOfT);
                    }
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _WRIdx) * _sizeOfT));

                    int PutCnt = _bufferSize - _WRIdx;
                    int remainCnt = len - PutCnt;
                    _WRIdx = 0;

                    dstPtr = _bufferPtr;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + (PutCnt + srcIdx) * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + (PutCnt + srcIdx) * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));

                    _WRIdx = remainCnt;
                }
                else
                {
                    //Buffer.BlockCopy(elements, 0, _buffer, _WRIdx * _sizeOfT, len * _sizeOfT);
                    var srcPtr = dataPtr;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt64() + srcIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(srcPtr.ToInt32() + srcIdx * _sizeOfT);
                    }
                    IntPtr dstPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt64() + _WRIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(_bufferPtr.ToInt32() + _WRIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(len * _sizeOfT));

                    if (_WRIdx + len == _bufferSize)
                    {
                        _WRIdx = 0;
                    }
                    else
                    {
                        _WRIdx += len;
                    }
                }
                _numOfElement += len;

                return len;
            }
        }

        /// <summary>
        /// 从缓冲队列中取出指定长度的数据
        /// </summary>
        /// <param name="reqBuffer">请求读取缓冲区</param>
        /// <returns>返回实际取到的数据长度</returns>
        public int Dequeue(ref T[] reqBuffer, int len)
        {
            lock (this)
            {
                int getCnt = len;

                if (len > _numOfElement || _numOfElement <= 0)
                {
                    return -1;
                }
                else if (len <= 0)
                {
                    getCnt = _numOfElement;
                }

                if (_RDIdx + getCnt > _bufferSize)   //取数据的总大小超过了应该分两次拷贝，先拷贝尾部，剩余的从头开始拷贝
                {
                    var gch = GCHandle.Alloc(reqBuffer, GCHandleType.Pinned);
                    var dstPtr = gch.AddrOfPinnedObject();
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _RDIdx) * _sizeOfT));
                    int fetchedCnt = (_bufferSize - _RDIdx);
                    int remainCnt = getCnt - fetchedCnt;
                    _RDIdx = 0;

                    //Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, fetchedCnt * _sizeOfT, remainCnt * _sizeOfT);
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + fetchedCnt * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + fetchedCnt * _sizeOfT);
                    }
                    srcPtr = _bufferPtr;
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _RDIdx) * _sizeOfT));

                    _RDIdx = remainCnt;
                }
                else
                {
                    var gch = GCHandle.Alloc(reqBuffer, GCHandleType.Pinned);
                    var dstPtr = gch.AddrOfPinnedObject();
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(getCnt * _sizeOfT));

                    if (_RDIdx + getCnt == _bufferSize)
                    {
                        _RDIdx = 0;
                    }
                    else
                    {
                        _RDIdx += getCnt;
                    }
                }
                _numOfElement -= getCnt;
                return getCnt;
            }
        }

        /// <summary>
        /// 从缓冲队列中取出指定长度的数据
        /// </summary>
        /// <param name="reqBuffer">请求读取缓冲区</param>
        /// <returns>返回实际取到的数据长度</returns>
        public int Dequeue(ref T[] reqBuffer, int dstIdx, int len)
        {
            lock (this)
            {
                int getCnt = len;

                if (len > _numOfElement || _numOfElement <= 0)
                {
                    return -1;
                }
                else if (len <= 0)
                {
                    getCnt = _numOfElement;
                }

                if (_RDIdx + getCnt > _bufferSize)   //取数据的总大小超过了应该分两次拷贝，先拷贝尾部，剩余的从头开始拷贝
                {
                    var gch = GCHandle.Alloc(reqBuffer, GCHandleType.Pinned);
                    var dstPtr = gch.AddrOfPinnedObject();
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + dstIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + dstIdx * _sizeOfT);
                    }
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _RDIdx) * _sizeOfT));
                    int fetchedCnt = (_bufferSize - _RDIdx);
                    int remainCnt = getCnt - fetchedCnt;
                    _RDIdx = 0;

                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + fetchedCnt * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + fetchedCnt * _sizeOfT);
                    }
                    srcPtr = _bufferPtr;
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));
                    gch.Free();
                    _RDIdx = remainCnt;
                }
                else
                {
                    var gch = GCHandle.Alloc(reqBuffer, GCHandleType.Pinned);
                    var dstPtr = gch.AddrOfPinnedObject();
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + dstIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + dstIdx * _sizeOfT);
                    }
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(getCnt * _sizeOfT));
                    gch.Free();
                    if (_RDIdx + getCnt == _bufferSize)
                    {
                        _RDIdx = 0;
                    }
                    else
                    {
                        _RDIdx += getCnt;
                    }
                }
                _numOfElement -= getCnt;
                return getCnt;
            }
        }

        /// <summary>
        /// 从缓冲队列中取出指定长度的数据
        /// </summary>
        /// <param name="reqBuffer">请求读取缓冲区</param>
        /// <returns>返回实际取到的数据长度</returns>
        public int Dequeue(ref T[,] reqBuffer, int dstIdx, int len)
        {
            lock (this)
            {
                int getCnt = len;

                if (len > _numOfElement || _numOfElement <= 0)
                {
                    return -1;
                }
                else if (len <= 0)
                {
                    getCnt = _numOfElement;
                }

                if (_RDIdx + getCnt > _bufferSize)   //取数据的总大小超过了应该分两次拷贝，先拷贝尾部，剩余的从头开始拷贝
                {
                    var gch = GCHandle.Alloc(reqBuffer, GCHandleType.Pinned);
                    var dstPtr = gch.AddrOfPinnedObject();
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + dstIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + dstIdx * _sizeOfT);
                    }
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _RDIdx) * _sizeOfT));
                    int fetchedCnt = (_bufferSize - _RDIdx);
                    int remainCnt = getCnt - fetchedCnt;
                    _RDIdx = 0;

                    //Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, fetchedCnt * _sizeOfT, remainCnt * _sizeOfT);
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + fetchedCnt * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + fetchedCnt * _sizeOfT);
                    }
                    srcPtr = _bufferPtr;
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));
                    gch.Free();
                    _RDIdx = remainCnt;
                }
                else
                {
                    //Buffer.BlockCopy(_buffer, _RDIdx * _sizeOfT, reqBuffer, 0, getCnt * _sizeOfT);
                    var gch = GCHandle.Alloc(reqBuffer, GCHandleType.Pinned);
                    var dstPtr = gch.AddrOfPinnedObject();
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + dstIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + dstIdx * _sizeOfT);
                    }
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(getCnt * _sizeOfT));
                    gch.Free();
                    if (_RDIdx + getCnt == _bufferSize)
                    {
                        _RDIdx = 0;
                    }
                    else
                    {
                        _RDIdx += getCnt;
                    }
                }
                _numOfElement -= getCnt;
                return getCnt;
            }
        }

        /// <summary>
        /// 从缓冲队列中取出指定长度的数据
        /// </summary>
        /// <param name="reqBuffer">请求读取缓冲区</param>
        /// <returns>返回实际取到的数据长度</returns>
        public int Dequeue(ref T[,] reqBuffer, int len)
        {
            lock (this)
            {
                int getCnt = len;

                if (len > _numOfElement || _numOfElement <= 0)
                {
                    return -1;
                }
                else if (len <= 0)
                {
                    getCnt = _numOfElement;
                }

                if (_RDIdx + getCnt > _bufferSize)   //取数据的总大小超过了应该分两次拷贝，先拷贝尾部，剩余的从头开始拷贝
                {
                    var gch = GCHandle.Alloc(reqBuffer, GCHandleType.Pinned);
                    var dstPtr = gch.AddrOfPinnedObject();
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _RDIdx) * _sizeOfT));
                    int fetchedCnt = (_bufferSize - _RDIdx);
                    int remainCnt = getCnt - fetchedCnt;
                    _RDIdx = 0;

                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + fetchedCnt * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + fetchedCnt * _sizeOfT);
                    }
                    srcPtr = _bufferPtr;
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));
                    gch.Free();
                    _RDIdx = remainCnt;
                }
                else
                {
                    var gch = GCHandle.Alloc(reqBuffer, GCHandleType.Pinned);
                    var dstPtr = gch.AddrOfPinnedObject();
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(getCnt * _sizeOfT));
                    gch.Free();
                    if (_RDIdx + getCnt == _bufferSize)
                    {
                        _RDIdx = 0;
                    }
                    else
                    {
                        _RDIdx += getCnt;
                    }
                }
                _numOfElement -= getCnt;
                return getCnt;
            }
        }

        /// <summary>
        /// 从缓冲队列中取出指定长度的数据
        /// </summary>
        /// <param name="reqBufferPtr">请求数据的首地址</param>
        /// <param name="dstIdx">目标数据的开始索引（以元素为单位，非字节单位）</param>
        /// <param name="len">出队数据的长度（以元素为单位，非字节单位）</param>
        /// <returns></returns>
        public int Dequeue(IntPtr reqBufferPtr, int dstIdx, int len)
        {
            lock (this)
            {
                int getCnt = len;

                if (len > _numOfElement || _numOfElement <= 0)
                {
                    return -1;
                }
                else if (len <= 0)
                {
                    getCnt = _numOfElement;
                }

                if (_RDIdx + getCnt > _bufferSize)   //取数据的总大小超过了应该分两次拷贝，先拷贝尾部，剩余的从头开始拷贝
                {
                    var dstPtr = reqBufferPtr;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + dstIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + dstIdx * _sizeOfT);
                    }
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)((_bufferSize - _RDIdx) * _sizeOfT));
                    int fetchedCnt = (_bufferSize - _RDIdx);
                    int remainCnt = getCnt - fetchedCnt;
                    _RDIdx = 0;

                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + fetchedCnt * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + fetchedCnt * _sizeOfT);
                    }
                    srcPtr = _bufferPtr;
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(remainCnt * _sizeOfT));
                    _RDIdx = remainCnt;
                }
                else
                {
                    var dstPtr = reqBufferPtr;
                    if (Environment.Is64BitProcess)
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt64() + dstIdx * _sizeOfT);
                    }
                    else
                    {
                        dstPtr = new IntPtr(dstPtr.ToInt32() + dstIdx * _sizeOfT);
                    }
                    IntPtr srcPtr = IntPtr.Zero;
                    if (Environment.Is64BitProcess)
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt64() + _RDIdx * _sizeOfT);
                    }
                    else
                    {
                        srcPtr = new IntPtr(_bufferPtr.ToInt32() + _RDIdx * _sizeOfT);
                    }
                    WinAPI.memcpy(dstPtr, srcPtr, (UIntPtr)(getCnt * _sizeOfT));

                    if (_RDIdx + getCnt == _bufferSize)
                    {
                        _RDIdx = 0;
                    }
                    else
                    {
                        _RDIdx += getCnt;
                    }
                }
                _numOfElement -= getCnt;
                return getCnt;
            }
        }
    }

    /// <summary>
    /// <para>循环缓冲链类，将CircularBufferEx链起来</para>
    /// <para>优点：不用预先分配较大的内存，当程序运行时不够用了再按块增加</para>
    /// <para>使用注意：块大小要合适，不能太小，也不能太大。</para>
    /// <para>----太小：增加内存拷贝的次数，消耗CPU资源；</para>
    /// <para>----太大：增加内存占用。</para>
    /// <para>推荐分配块大小为入队和出队数据量的4~16倍</para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class CircularBufferList<T>
    {
        /// <summary>
        /// 元素个数计数
        /// </summary>
        private int _numOfElement;

        /// <summary>
        /// 当前元素个数
        /// </summary>
        public int NumOfElement
        {
            get
            {
                lock (this)
                {
                    return _numOfElement;
                }
            }
        }

        private int _currentCapacity;
        /// <summary>
        /// 当前缓冲区容量
        /// </summary>
        public int CurrentCapacity
        {
            get
            {
                lock (this)
                {
                    return _currentCapacity;
                }
            }
        }

        /// <summary>
        /// 块大小
        /// </summary>
        private int _blockSize;

        /// <summary>
        /// 循环缓冲链表
        /// </summary>
        private List<CircularBufferEx<T>> _lstCircleBuffer;

        /// <summary>
        /// 读写索引
        /// </summary>
        private int _RDBlockIdx, _WRBlockIdx;

        /// <summary>
        /// 上一次Dequeue后的块数
        /// </summary>
        private int _LastBlockCount;

        /// <summary>
        /// 第一次Dequeue后标志为false
        /// </summary>
        private bool _firstDequeueFlag;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="blockSize"></param>
        public CircularBufferList(int blockSize)
        {
            _lstCircleBuffer = new List<CircularBufferEx<T>>();
            _lstCircleBuffer.Add(new CircularBufferEx<T>(blockSize));
            _blockSize = blockSize;
            _currentCapacity = blockSize;
            _numOfElement = 0;
            _RDBlockIdx = 0;
            _WRBlockIdx = 0;
            _LastBlockCount = 1;
            _firstDequeueFlag = true;
        }

        /// <summary>
        /// 清空循环缓冲区的数据
        /// </summary>
        public void Clear()
        {
            lock (this)
            {
                if (_lstCircleBuffer.Count > 0)
                {
                    foreach (var item in _lstCircleBuffer)
                    {
                        item.Clear(); //清空并重置块
                    }
                    _currentCapacity += _numOfElement;           
                    _numOfElement = 0;
                    _RDBlockIdx = 0;
                    _WRBlockIdx = 0;
                }
            }
        }

        /// <summary>
        /// 释放循环缓冲区
        /// </summary>
        public void Dispose()
        {
            lock (this)
            {
                if (_lstCircleBuffer.Count > 0)
                {
                    foreach (var item in _lstCircleBuffer)
                    {
                        item.Dispose(); //释放块
                    }
                    _lstCircleBuffer.Clear(); //清空缓冲链
                    _numOfElement = 0;
                    _RDBlockIdx = 0;
                    _WRBlockIdx = 0;
                    _currentCapacity = _lstCircleBuffer.Count * _blockSize;
                }
            }
        }

        /// <summary>
        /// 析构函数，避免非托管内存没有释放
        /// </summary>
        ~CircularBufferList()
        {
            Dispose();
        }

        /// <summary>
        /// 数据入队
        /// </summary>
        /// <param name="elems">入队的数据源数组</param>
        /// <returns></returns>
        public int Enqueue(T[] elems)
        {
            int toEqLen = 0;
            int eqedLen = 0;
            int srcIdx = 0;
            int gap = 0;
            lock (this)
            {
                while (eqedLen < elems.Length)
                {
                    toEqLen = elems.Length - eqedLen;
                    gap = _lstCircleBuffer.Count >= 2 ? _lstCircleBuffer[_RDBlockIdx].CurrentCapacity : 0;
                    if (_currentCapacity - gap >= toEqLen)  //容量足够(此处要留余量)
                    {
                        if (_lstCircleBuffer[_WRBlockIdx].CurrentCapacity >= toEqLen) //当前块容量足够,直接存入
                        {
                            _lstCircleBuffer[_WRBlockIdx].Enqueue(elems, srcIdx, toEqLen);
                            _currentCapacity -= toEqLen;
                            _numOfElement += toEqLen;
                            eqedLen += toEqLen;
                            break;
                        }
                        else //当前块容量不够，先存入一部分，然后再到下一次循环存入
                        {
                            int cap = _lstCircleBuffer[_WRBlockIdx].CurrentCapacity;
                            _lstCircleBuffer[_WRBlockIdx].Enqueue(elems, srcIdx, cap);

                            _currentCapacity -= cap;
                            _numOfElement += cap;
                            eqedLen += cap;
                            srcIdx += cap;

                            if (_WRBlockIdx == _lstCircleBuffer.Count - 1)
                            {
                                _WRBlockIdx = 0;
                            }
                            else
                            {
                                _WRBlockIdx++;
                            }
                        }
                    }
                    else
                    {
                        _lstCircleBuffer.Insert(_WRBlockIdx + 1, new CircularBufferEx<T>(_blockSize));
                        _currentCapacity += _blockSize;
                        if (_RDBlockIdx >= _WRBlockIdx + 1) //如果块插入到了读指针的前面，则读指针要+1
                        {
                            _RDBlockIdx++;
                        }
                    }
                }
            }
            return eqedLen;
        }

        /// <summary>
        /// 数据入队，带数据源的开始索引
        /// </summary>
        /// <param name="elems">入队的数据源数组</param>
        /// <param name="srcIdx">数据源的开始索引</param>
        /// <param name="len">入队的数据长度</param>
        /// <returns></returns>
        public int Enqueue(T[] elems, int srcIdx, int len)
        {
            int toEqLen = 0;
            int eqedLen = 0;
            int srcIdx1 = 0;
            int gap = 0;
            lock (this)
            {
                while (eqedLen < len)
                {
                    toEqLen = len - eqedLen;
                    gap = _lstCircleBuffer.Count >= 2 ? _lstCircleBuffer[_RDBlockIdx].CurrentCapacity : 0;
                    if (_currentCapacity - gap >= toEqLen)  //容量足够(此处要留余量)
                    {
                        if (_lstCircleBuffer[_WRBlockIdx].CurrentCapacity >= toEqLen) //当前块容量足够,直接存入
                        {
                            _lstCircleBuffer[_WRBlockIdx].Enqueue(elems, srcIdx1 + srcIdx, toEqLen);
                            _currentCapacity -= toEqLen;
                            _numOfElement += toEqLen;
                            eqedLen += toEqLen;
                            break;
                        }
                        else //当前块容量不够，先存入一部分，然后再到下一次循环存入
                        {
                            int cap = _lstCircleBuffer[_WRBlockIdx].CurrentCapacity;
                            _lstCircleBuffer[_WRBlockIdx].Enqueue(elems, srcIdx1 + srcIdx, cap);

                            _currentCapacity -= cap;
                            _numOfElement += cap;
                            eqedLen += cap;
                            srcIdx1 += cap;

                            if (_WRBlockIdx == _lstCircleBuffer.Count - 1)
                            {
                                _WRBlockIdx = 0;
                            }
                            else
                            {
                                _WRBlockIdx++;
                            }
                        }
                    }
                    else
                    {
                        _lstCircleBuffer.Insert(_WRBlockIdx + 1, new CircularBufferEx<T>(_blockSize));
                        _currentCapacity += _blockSize;
                        if (_RDBlockIdx >= _WRBlockIdx + 1) //如果块插入到了读指针的前面，则读指针要+1
                        {
                            _RDBlockIdx++;
                        }
                    }
                }
            }
            return eqedLen;
        }

        /// <summary>
        /// 数据入队，不带数据源的开始索引
        /// </summary>
        /// <param name="elems">入队的数据源数组</param>
        /// <param name="len">入队的数据长度</param>
        /// <returns></returns>
        public int Enqueue(T[] elems, int len)
        {
            return Enqueue(elems, 0, len);
        }

        /// <summary>
        /// 数据入队，带数据源的开始索引
        /// </summary>
        /// <param name="elemsPtr">入队的数据源数组地址</param>
        /// <param name="srcIdx">数据源的开始索引（以元素为单位，非字节）</param>
        /// <param name="len">入队的数据长度（以元素为单位，非字节）</param>
        /// <returns></returns>
        public int Enqueue(IntPtr elemsPtr, int srcIdx, int len)
        {
            int toEqLen = 0;
            int eqedLen = 0;
            int srcIdx1 = 0;
            int gap = 0;
            lock (this)
            {
                while (eqedLen < len)
                {
                    toEqLen = len - eqedLen;
                    gap = _lstCircleBuffer.Count >= 2 ? _lstCircleBuffer[_RDBlockIdx].CurrentCapacity : 0;
                    if (_currentCapacity - gap >= toEqLen)  //容量足够(此处要留余量)
                    {
                        if (_lstCircleBuffer[_WRBlockIdx].CurrentCapacity >= toEqLen) //当前块容量足够,直接存入
                        {
                            _lstCircleBuffer[_WRBlockIdx].Enqueue(elemsPtr, srcIdx1 + srcIdx, toEqLen);
                            _currentCapacity -= toEqLen;
                            _numOfElement += toEqLen;
                            eqedLen += toEqLen;
                            break;
                        }
                        else //当前块容量不够，先存入一部分，然后再到下一次循环存入
                        {
                            int cap = _lstCircleBuffer[_WRBlockIdx].CurrentCapacity;
                            _lstCircleBuffer[_WRBlockIdx].Enqueue(elemsPtr, srcIdx1 + srcIdx, cap);

                            _currentCapacity -= cap;
                            _numOfElement += cap;
                            eqedLen += cap;
                            srcIdx1 += cap;

                            if (_WRBlockIdx == _lstCircleBuffer.Count - 1)
                            {
                                _WRBlockIdx = 0;
                            }
                            else
                            {
                                _WRBlockIdx++;
                            }
                        }
                    }
                    else
                    {
                        _lstCircleBuffer.Insert(_WRBlockIdx + 1, new CircularBufferEx<T>(_blockSize));
                        _currentCapacity += _blockSize;
                        if (_RDBlockIdx >= _WRBlockIdx + 1) //如果块插入到了读指针的前面，则读指针要+1
                        {
                            _RDBlockIdx++;
                        }
                    }
                }
            }
            return eqedLen;
        }

        /// <summary>
        /// 数据入队，带数据源的开始索引
        /// </summary>
        /// <param name="elems">入队的数据源数组</param>
        /// <param name="srcIdx">数据源的开始索引</param>
        /// <param name="len">入队的数据长度</param>
        /// <returns></returns>
        public int Enqueue(T[,] elems, int srcIdx, int len)
        {
            int toEqLen = 0;
            int eqedLen = 0;
            int srcIdx1 = 0;
            int gap = 0;
            lock(this)
            { 
                while (eqedLen < len)
                {
                    toEqLen = len - eqedLen;
                    gap = _lstCircleBuffer.Count >= 2 ? _lstCircleBuffer[_RDBlockIdx].CurrentCapacity : 0;
                    if (_currentCapacity - gap >= toEqLen)  //容量足够(此处要留余量)
                    {
                        if (_lstCircleBuffer[_WRBlockIdx].CurrentCapacity >= toEqLen) //当前块容量足够,直接存入
                        {
                            _lstCircleBuffer[_WRBlockIdx].Enqueue(elems, srcIdx1 + srcIdx, toEqLen);
                            _currentCapacity -= toEqLen;
                            _numOfElement += toEqLen;
                            eqedLen += toEqLen;
                            break;
                        }
                        else //当前块容量不够，先存入一部分，然后再到下一次循环存入
                        {
                            int cap = _lstCircleBuffer[_WRBlockIdx].CurrentCapacity;
                            _lstCircleBuffer[_WRBlockIdx].Enqueue(elems, srcIdx1 + srcIdx, cap);

                            _currentCapacity -= cap;
                            _numOfElement += cap;
                            eqedLen += cap;
                            srcIdx1 += cap;

                            if (_WRBlockIdx == _lstCircleBuffer.Count - 1)
                            {
                                _WRBlockIdx = 0;
                            }
                            else
                            {
                                _WRBlockIdx++;
                            }
                        }
                    }
                    else
                    {
                        _lstCircleBuffer.Insert(_WRBlockIdx + 1, new CircularBufferEx<T>(_blockSize));
                        _currentCapacity += _blockSize;
                        if (_RDBlockIdx >= _WRBlockIdx + 1) //如果块插入到了读指针的前面，则读指针要+1
                        {
                            _RDBlockIdx++;
                        }
                    }
                }
            }
            return eqedLen;
        }

        /// <summary>
        /// 数据入队，不带数据源的开始索引
        /// </summary>
        /// <param name="elems">入队的数据源数组</param>
        /// <param name="len">入队的数据长度</param>
        /// <returns></returns>
        public int Enqueue(T[,] elems, int len)
        {
            return Enqueue(elems, 0, len);
        }

        /// <summary>
        /// 数据出队
        /// </summary>
        /// <param name="retBuffer">请求出队的目标数组</param>
        /// <returns></returns>
        public int Dequeue(ref T[] retBuffer)
        {
            int toDqLen = 0;
            int dqedLen = 0;
            int dstIdx = 0;
            lock (this)
            {
                int beforeCapacity = _currentCapacity;
                lock (this)
                {
                    while (dqedLen < retBuffer.Length)
                    {
                        toDqLen = retBuffer.Length - dqedLen;
                        if (_numOfElement >= toDqLen)  //数据足够
                        {
                            if (_lstCircleBuffer[_RDBlockIdx].NumOfElement >= toDqLen) //当前块的数据足够,直接取出
                            {
                                _lstCircleBuffer[_RDBlockIdx].Dequeue(ref retBuffer, dstIdx, toDqLen);
                                _currentCapacity += toDqLen;
                                _numOfElement -= toDqLen;
                                dqedLen += toDqLen;

                                if (_currentCapacity > 2 * _blockSize //空闲的块超过2个
                                    && beforeCapacity > _blockSize    //Dequeue前空闲的快速超过1个
                                    && _firstDequeueFlag == false     //不是第一次Deuque
                                    && _lstCircleBuffer.Count == _LastBlockCount)  //块数没变
                                {
                                    //满足以上四个条件则释放一块
                                    // :: :: :R:::::::::W: :: :: ::
                                    if (_RDBlockIdx <= _WRBlockIdx) //写块索引在后面，可以释放读块前面的，或后面的
                                    {
                                        if (_RDBlockIdx > 0) //读索引大于0，则可以释放第0块
                                        {
                                            _RDBlockIdx--;  //移除前面的，读写索引都要减1
                                            _WRBlockIdx--;
                                            _currentCapacity -= _blockSize;
                                            _lstCircleBuffer[0].Dispose();
                                            _lstCircleBuffer.RemoveAt(0); //释放后移除
                                        }
                                        else if (_WRBlockIdx < _lstCircleBuffer.Count - 1)
                                        {
                                            _currentCapacity -= _blockSize;

                                            //移除后面的，读写索引都不减1
                                            _lstCircleBuffer[_lstCircleBuffer.Count - 1].Dispose();
                                            _lstCircleBuffer.RemoveAt(_lstCircleBuffer.Count - 1); //释放后移除
                                        }
                                    }
                                    else // ::::W:: :: :: ::R:::::::
                                    {
                                        _RDBlockIdx--;  //移除中间的，读索引要减1，写索引不变
                                        _currentCapacity -= _blockSize;
                                        _lstCircleBuffer[_WRBlockIdx + 1].Dispose();
                                        _lstCircleBuffer.RemoveAt(_WRBlockIdx + 1); //释放后移除
                                    }
                                }
                                _LastBlockCount = _lstCircleBuffer.Count;
                                _firstDequeueFlag = false;
                                break;
                            }
                            else //当前块内的数据不够，先读取一部分，然后再到下一次循环读取
                            {
                                int cnt = _lstCircleBuffer[_RDBlockIdx].NumOfElement;
                                _lstCircleBuffer[_RDBlockIdx].Dequeue(ref retBuffer, dstIdx, cnt);
                                _currentCapacity += cnt;
                                _numOfElement -= cnt;
                                dqedLen += cnt;

                                dstIdx += cnt;

                                if (_RDBlockIdx == _lstCircleBuffer.Count - 1)
                                {
                                    _RDBlockIdx = 0;
                                }
                                else
                                {
                                    _RDBlockIdx++;
                                }
                            }
                        }
                        else
                        {
                            return 0;
                        }
                    }
                }
            }
            return dqedLen;
        }

        /// <summary>
        /// 数据出队，带目标数组的开始索引
        /// </summary>
        /// <param name="retBuffer">请求出队的目标数组</param>
        /// <param name="dstIdx">目标数组的开始索引</param>
        /// <param name="len">出队的数据长度</param>
        /// <returns></returns>
        public int Dequeue(ref T[] retBuffer, int dstIdx, int len)
        {
            int toDqLen = 0;
            int dqedLen = 0;
            int dstIdx1 = 0;
            lock (this)
            {
                int beforeCapacity = _currentCapacity;
                lock (this)
                {
                    while (dqedLen < retBuffer.Length)
                    {
                        toDqLen = len - dqedLen;
                        if (_numOfElement >= toDqLen)  //数据足够
                        {
                            if (_lstCircleBuffer[_RDBlockIdx].NumOfElement >= toDqLen) //当前块的数据足够,直接取出
                            {
                                _lstCircleBuffer[_RDBlockIdx].Dequeue(ref retBuffer, dstIdx1 + dstIdx, toDqLen);
                                _currentCapacity += toDqLen;
                                _numOfElement -= toDqLen;
                                dqedLen += toDqLen;

                                if (_currentCapacity > 2 * _blockSize //空闲的块超过2个
                                    && beforeCapacity > _blockSize    //Dequeue前空闲的快速超过1个
                                    && _firstDequeueFlag == false     //不是第一次Deuque
                                    && _lstCircleBuffer.Count == _LastBlockCount)  //块数没变
                                {
                                    //满足以上四个条件则释放一块
                                    // :: :: :R:::::::::W: :: :: ::
                                    if (_RDBlockIdx <= _WRBlockIdx) //写块索引在后面，可以释放读块前面的，或后面的
                                    {
                                        if (_RDBlockIdx > 0) //读索引大于0，则可以释放第0块
                                        {
                                            _RDBlockIdx--;  //移除前面的，读写索引都要减1
                                            _WRBlockIdx--;
                                            _currentCapacity -= _blockSize;
                                            _lstCircleBuffer[0].Dispose();
                                            _lstCircleBuffer.RemoveAt(0); //释放后移除
                                        }
                                        else if (_WRBlockIdx < _lstCircleBuffer.Count - 1)
                                        {
                                            _currentCapacity -= _blockSize;

                                            //移除后面的，读写索引都不减1
                                            _lstCircleBuffer[_lstCircleBuffer.Count - 1].Dispose();
                                            _lstCircleBuffer.RemoveAt(_lstCircleBuffer.Count - 1); //释放后移除
                                        }
                                    }
                                    else // ::::W:: :: :: ::R:::::::
                                    {
                                        _RDBlockIdx--;  //移除中间的，读索引要减1，写索引不变
                                        _currentCapacity -= _blockSize;
                                        _lstCircleBuffer[_WRBlockIdx + 1].Dispose();
                                        _lstCircleBuffer.RemoveAt(_WRBlockIdx + 1); //释放后移除
                                    }
                                }
                                _LastBlockCount = _lstCircleBuffer.Count;
                                _firstDequeueFlag = false;
                                break;
                            }
                            else //当前块内的数据不够，先读取一部分，然后再到下一次循环读取
                            {
                                int cnt = _lstCircleBuffer[_RDBlockIdx].NumOfElement;
                                _lstCircleBuffer[_RDBlockIdx].Dequeue(ref retBuffer, dstIdx1 + dstIdx, cnt);
                                _currentCapacity += cnt;
                                _numOfElement -= cnt;
                                dqedLen += cnt;

                                dstIdx1 += cnt;

                                if (_RDBlockIdx == _lstCircleBuffer.Count - 1)
                                {
                                    _RDBlockIdx = 0;
                                }
                                else
                                {
                                    _RDBlockIdx++;
                                }
                            }
                        }
                        else
                        {
                            return 0;
                        }
                    }
                }
            }
            return dqedLen;
        }

        /// <summary>
        /// 数据出队，不带目标数组的开始索引
        /// </summary>
        /// <param name="retBuffer">请求出队的目标数组</param>
        /// <param name="len">出队的数据长度</param>
        /// <returns></returns>
        public int Dequeue(ref T[] retBuffer, int len)
        {
            return Dequeue(ref retBuffer, 0, len);
        }

        /// <summary>
        /// 数据出队，带目标数组的开始索引
        /// </summary>
        /// <param name="retBuffer">请求出队的目标数组</param>
        /// <param name="dstIdx">目标数组的开始索引</param>
        /// <param name="len">出队的数据长度</param>
        /// <returns></returns>
        public int Dequeue(ref T[,] retBuffer, int dstIdx, int len)
        {
            int toDqLen = 0;
            int dqedLen = 0;
            int dstIdx1 = 0;
            lock (this)
            {
                int beforeCapacity = _currentCapacity;
                while (dqedLen < retBuffer.Length)
                {
                    toDqLen = len - dqedLen;
                    if (_numOfElement >= toDqLen)  //数据足够
                    {
                        if (_lstCircleBuffer[_RDBlockIdx].NumOfElement >= toDqLen) //当前块的数据足够,直接取出
                        {
                            _lstCircleBuffer[_RDBlockIdx].Dequeue(ref retBuffer, dstIdx1 + dstIdx, toDqLen);
                            _currentCapacity += toDqLen;
                            _numOfElement -= toDqLen;
                            dqedLen += toDqLen;

                            if (_currentCapacity > 2 * _blockSize //空闲的块超过2个
                                && beforeCapacity > _blockSize    //Dequeue前空闲的快速超过1个
                                && _firstDequeueFlag == false     //不是第一次Deuque
                                && _lstCircleBuffer.Count == _LastBlockCount)  //块数没变
                            {
                                //满足以上四个条件则释放一块
                                // :: :: :R:::::::::W: :: :: ::
                                if (_RDBlockIdx <= _WRBlockIdx) //写块索引在后面，可以释放读块前面的，或后面的
                                {
                                    if (_RDBlockIdx > 0) //读索引大于0，则可以释放第0块
                                    {
                                        _RDBlockIdx--;  //移除前面的，读写索引都要减1
                                        _WRBlockIdx--;
                                        _currentCapacity -= _blockSize;
                                        _lstCircleBuffer[0].Dispose();
                                        _lstCircleBuffer.RemoveAt(0); //释放后移除
                                    }
                                    else if (_WRBlockIdx < _lstCircleBuffer.Count - 1)
                                    {
                                        _currentCapacity -= _blockSize;

                                        //移除后面的，读写索引都不减1
                                        _lstCircleBuffer[_lstCircleBuffer.Count - 1].Dispose();
                                        _lstCircleBuffer.RemoveAt(_lstCircleBuffer.Count - 1); //释放后移除
                                    }
                                }
                                else // ::::W:: :: :: ::R:::::::
                                {
                                    _RDBlockIdx--;  //移除中间的，读索引要减1，写索引不变
                                    _currentCapacity -= _blockSize;
                                    _lstCircleBuffer[_WRBlockIdx + 1].Dispose();
                                    _lstCircleBuffer.RemoveAt(_WRBlockIdx + 1); //释放后移除
                                }
                            }
                            _LastBlockCount = _lstCircleBuffer.Count;
                            _firstDequeueFlag = false;
                            break;
                        }
                        else //当前块内的数据不够，先读取一部分，然后再到下一次循环读取
                        {
                            int cnt = _lstCircleBuffer[_RDBlockIdx].NumOfElement;
                            _lstCircleBuffer[_RDBlockIdx].Dequeue(ref retBuffer, dstIdx1 + dstIdx, cnt);
                            _currentCapacity += cnt;
                            _numOfElement -= cnt;
                            dqedLen += cnt;

                            dstIdx1 += cnt;

                            if (_RDBlockIdx == _lstCircleBuffer.Count - 1)
                            {
                                _RDBlockIdx = 0;
                            }
                            else
                            {
                                _RDBlockIdx++;
                            }
                        }
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
            return dqedLen;
        }

        /// <summary>
        /// 数据出队，不带目标数组的开始索引
        /// </summary>
        /// <param name="retBuffer">请求出队的目标数组</param>
        /// <param name="len">出队的数据长度</param>
        /// <returns></returns>
        public int Dequeue(ref T[,] retBuffer, int len)
        {
            return Dequeue(ref retBuffer, 0, len);
        }

        /// <summary>
        /// 数据出队，带目标数组的开始索引
        /// </summary>
        /// <param name="retBufferPtr">请求出队的目标数组地址</param>
        /// <param name="dstIdx">目标数组的开始索引（以元素为单位，非字节）</param>
        /// <param name="len">出队的数据长度（以元素为单位，非字节）</param>
        /// <returns></returns>
        public int Dequeue(IntPtr retBufferPtr, int dstIdx, int len)
        {
            int toDqLen = 0;
            int dqedLen = 0;
            int dstIdx1 = 0;
            lock (this)
            {
                int beforeCapacity = _currentCapacity;
                while (dqedLen < len)
                {
                    toDqLen = len - dqedLen;
                    if (_numOfElement >= toDqLen)  //数据足够
                    {
                        if (_lstCircleBuffer[_RDBlockIdx].NumOfElement >= toDqLen) //当前块的数据足够,直接取出
                        {
                            _lstCircleBuffer[_RDBlockIdx].Dequeue(retBufferPtr, dstIdx1 + dstIdx, toDqLen);
                            _currentCapacity += toDqLen;
                            _numOfElement -= toDqLen;
                            dqedLen += toDqLen;

                            if (_currentCapacity > 2 * _blockSize //空闲的块超过2个
                                && beforeCapacity > _blockSize    //Dequeue前空闲的快速超过1个
                                && _firstDequeueFlag == false     //不是第一次Deuque
                                && _lstCircleBuffer.Count == _LastBlockCount)  //块数没变
                            {
                                //满足以上四个条件则释放一块
                                // :: :: :R:::::::::W: :: :: ::
                                if (_RDBlockIdx <= _WRBlockIdx) //写块索引在后面，可以释放读块前面的，或后面的
                                {
                                    if (_RDBlockIdx > 0) //读索引大于0，则可以释放第0块
                                    {
                                        _RDBlockIdx--;  //移除前面的，读写索引都要减1
                                        _WRBlockIdx--;
                                        _currentCapacity -= _blockSize;
                                        _lstCircleBuffer[0].Dispose();
                                        _lstCircleBuffer.RemoveAt(0); //释放后移除
                                    }
                                    else if (_WRBlockIdx < _lstCircleBuffer.Count - 1)
                                    {
                                        _currentCapacity -= _blockSize;

                                        //移除后面的，读写索引都不减1
                                        _lstCircleBuffer[_lstCircleBuffer.Count - 1].Dispose();
                                        _lstCircleBuffer.RemoveAt(_lstCircleBuffer.Count - 1); //释放后移除
                                    }
                                }
                                else // ::::W:: :: :: ::R:::::::
                                {
                                    _RDBlockIdx--;  //移除中间的，读索引要减1，写索引不变
                                    _currentCapacity -= _blockSize;
                                    _lstCircleBuffer[_WRBlockIdx + 1].Dispose();
                                    _lstCircleBuffer.RemoveAt(_WRBlockIdx + 1); //释放后移除
                                }
                            }
                            _LastBlockCount = _lstCircleBuffer.Count;
                            _firstDequeueFlag = false;
                            break;
                        }
                        else //当前块内的数据不够，先读取一部分，然后再到下一次循环读取
                        {
                            int cnt = _lstCircleBuffer[_RDBlockIdx].NumOfElement;
                            _lstCircleBuffer[_RDBlockIdx].Dequeue(retBufferPtr, dstIdx1 + dstIdx, cnt);
                            _currentCapacity += cnt;
                            _numOfElement -= cnt;
                            dqedLen += cnt;

                            dstIdx1 += cnt;

                            if (_RDBlockIdx == _lstCircleBuffer.Count - 1)
                            {
                                _RDBlockIdx = 0;
                            }
                            else
                            {
                                _RDBlockIdx++;
                            }
                        }
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
            return dqedLen;
        }
    }
    #endregion
}
