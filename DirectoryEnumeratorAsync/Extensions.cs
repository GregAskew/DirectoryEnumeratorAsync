namespace DirectoryEnumeratorAsync {

    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Data.SqlClient;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks; 
    #endregion

    public static class Extensions {

        /// <summary>
        /// Stack trace, target site, and error message of outer and inner exception, formatted with newlines
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="exception"></param>
        /// <returns></returns>
        [DebuggerStepThroughAttribute]
        public static string VerboseExceptionString<T>(this T exception) where T : Exception {
            var exceptionString = new StringBuilder();

            exceptionString.AppendLine($" Exception: {exception.GetType().Name} Message: {exception.Message ?? "NULL"}");
            exceptionString.AppendLine($" StackTrace: {exception.StackTrace ?? "NULL"}");
            exceptionString.AppendLine($" TargetSite: {(exception.TargetSite != null ? exception.TargetSite.ToString() : "NULL")}");

            if (exception.InnerException != null) {
                exceptionString.AppendLine();
                exceptionString.AppendLine("Inner Exception:");
                exceptionString.AppendLine(exception.InnerException.VerboseExceptionString());
            }

            return exceptionString.ToString();
        }

        [DebuggerStepThroughAttribute]
        public static string CurrentMethodName() {
            var frame = new StackFrame(1);
            var method = frame.GetMethod();
            var type = method.DeclaringType;
            var name = method.Name;

            return type + "::" + name + "(): ";
        }

        [DebuggerStepThroughAttribute]
        public static string YMDFriendly(this DateTime dateTime) {
            return dateTime.ToString("yyyy-MM-dd");
        }

        [DebuggerStepThroughAttribute]
        public static string YMDHMFriendly(this DateTime dateTime) {
            return dateTime.ToString("yyyy-MM-dd HH:mm");
        }

        [DebuggerStepThroughAttribute]
        public static string YMDHMSFriendly(this DateTime dateTime) {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// Returns a TimeSpan formatted in DD:HH:mm:ss
        /// </summary>
        /// <param name="timeSpan">The TimeSpan</param>
        /// <returns>The formatted string</returns>
        [DebuggerStepThroughAttribute]
        public static string DHMSFriendly(this TimeSpan timeSpan) {
            return string.Format("{0:00}.{1:00}:{2:00}:{3:00}", timeSpan.Days, timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
        }

        /// <summary>
        /// Returns a TimeSpan formatted in HH:mm:ss
        /// </summary>
        /// <param name="timeSpan">The TimeSpan</param>
        /// <returns>The formatted string</returns>
        [DebuggerStepThroughAttribute]
        public static string HMSFriendly(this TimeSpan timeSpan) {
            return string.Format("{0:00}:{1:00}:{2:00}", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
        }
    }
}
