using System;
namespace CYPCore.Helper
{
    public class TaskResult<T>
    {
        public TaskResult()
        {

        }

        public bool Success { get; private set; }
        public T Value { get; private set; }
        public dynamic NonSuccessMessage { get; private set; }
        public Exception Exception { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public static TaskResult<T> CreateSuccess(T result)
        {
            return new TaskResult<T> { Success = result != null, Value = result };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="successMessage"></param>
        /// <returns></returns>
        public static TaskResult<T> CreateSuccess(dynamic successMessage)
        {
            return new TaskResult<T> { Success = successMessage != null, Value = successMessage };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nonSuccessMessage"></param>
        /// <returns></returns>
        public static TaskResult<T> CreateFailure(dynamic nonSuccessMessage)
        {
            return new TaskResult<T> { Success = false, Value = default, NonSuccessMessage = nonSuccessMessage };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        public static TaskResult<T> CreateFailure(Exception ex)
        {
            return new TaskResult<T>
            {
                Success = false,
                NonSuccessMessage = $"{ex.Message}{Environment.NewLine}{ex.StackTrace}",
                Exception = ex,
                Value = default,
            };
        }
    }
}
