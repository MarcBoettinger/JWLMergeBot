using System;
using System.Collections.Generic;

namespace JWLMergeBot.Helpers
{
    /// <summary>
    /// If in a chat more than MaxMessages are received within the EvalutationPeriod, the message will be dropped
    /// </summary>
    internal class Banner
    {
        #region Ban Logic
        private static long EvalutationPeriod = 60;
        private static long MaxMessages = 10;
        #endregion

        private static Dictionary<long, List<long>> messagesCount = new Dictionary<long, List<long>>();

        /// <summary>
        /// Mark that this chat has communicated
        /// Check if this chatId has to be banned
        /// </summary>
        /// <param name="chatId"></param>
        /// <returns>True if the chat has been banned</returns>
        public static bool CheckIsChatBanned(long chatId)
        {
            // Add the current timestamp for this chat
            long currentTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            if(!messagesCount.ContainsKey(chatId))
                messagesCount.Add(chatId, new List<long>());
            messagesCount[chatId].Add(currentTimestamp);

            // Remove all timestamp that preceeds the EvalutationPeriod
            while (messagesCount[chatId].Count > 0 && messagesCount[chatId][0] < currentTimestamp - EvalutationPeriod)
                messagesCount[chatId].RemoveAt(0);

            // Return wheter the messages count in the Period exceeds the MaxMessages
            return messagesCount[chatId].Count > MaxMessages;
        }
    }
}
