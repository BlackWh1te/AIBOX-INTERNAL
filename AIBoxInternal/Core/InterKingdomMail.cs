using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIBoxInternal.Core
{
    /// <summary>
    /// A persistent mail message between two kingdoms.
    /// Messages survive across AI cycles and form conversation threads.
    /// </summary>
    [Serializable]
    public class InterKingdomMailMessage
    {
        public int Id;
        public string SenderKingdom;
        public string RecipientKingdom;
        public string Subject;
        public string Body;
        public float Timestamp;
        public bool IsRead;
        public bool IsDelivered;      // true once the recipient AI has "seen" it in a prompt
        public bool IsReplied;        // true if the recipient sent a reply
        public float OpinionShift;    // how much this message shifted diplomatic opinion
    }

    /// <summary>
    /// Global mail registry. Persists across all kingdoms and cycles.
    /// </summary>
    public static class MailRegistry
    {
        public static List<InterKingdomMailMessage> AllMessages = new List<InterKingdomMailMessage>();
        private static int _nextId = 1;
        private const float MESSAGE_TTL = 600f; // 10 minutes real time
        private const float THREAD_TTL = 1200f; // 20 minutes real time

        /// <summary>
        /// Send a new message from sender to recipient.
        /// </summary>
        public static InterKingdomMailMessage Send(string sender, string recipient, string subject, string body, float opinionShift = 0f)
        {
            var msg = new InterKingdomMailMessage
            {
                Id = _nextId++,
                SenderKingdom = sender,
                RecipientKingdom = recipient,
                Subject = subject ?? "No Subject",
                Body = body ?? "",
                Timestamp = Time.time,
                IsRead = false,
                IsDelivered = false,
                IsReplied = false,
                OpinionShift = opinionShift
            };
            AllMessages.Add(msg);
            CleanupOldMessages();
            return msg;
        }

        /// <summary>
        /// Get all unread messages for a kingdom.
        /// </summary>
        public static List<InterKingdomMailMessage> GetUnreadInbox(string recipient)
        {
            return AllMessages
                .Where(m => m.RecipientKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase) && !m.IsRead)
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Get all messages sent BY a kingdom.
        /// </summary>
        public static List<InterKingdomMailMessage> GetSent(string sender)
        {
            return AllMessages
                .Where(m => m.SenderKingdom.Equals(sender, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Get the full conversation thread between two kingdoms (both directions).
        /// </summary>
        public static List<InterKingdomMailMessage> GetThread(string kingdomA, string kingdomB)
        {
            return AllMessages
                .Where(m =>
                    (m.SenderKingdom.Equals(kingdomA, StringComparison.OrdinalIgnoreCase) && m.RecipientKingdom.Equals(kingdomB, StringComparison.OrdinalIgnoreCase)) ||
                    (m.SenderKingdom.Equals(kingdomB, StringComparison.OrdinalIgnoreCase) && m.RecipientKingdom.Equals(kingdomA, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Mark all unread messages for a kingdom as delivered (seen by AI this cycle).
        /// Returns the count of newly delivered messages.
        /// </summary>
        public static int MarkDelivered(string recipient)
        {
            int count = 0;
            foreach (var m in AllMessages)
            {
                if (!m.IsRead && !m.IsDelivered &&
                    m.RecipientKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase))
                {
                    m.IsDelivered = true;
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Mark a specific message as read.
        /// </summary>
        public static void MarkRead(int messageId)
        {
            var msg = AllMessages.FirstOrDefault(m => m.Id == messageId);
            if (msg != null) msg.IsRead = true;
        }

        /// <summary>
        /// Mark all messages from a specific sender as read.
        /// </summary>
        public static void MarkAllReadFrom(string recipient, string sender)
        {
            foreach (var m in AllMessages)
            {
                if (!m.IsRead &&
                    m.RecipientKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase) &&
                    m.SenderKingdom.Equals(sender, StringComparison.OrdinalIgnoreCase))
                {
                    m.IsRead = true;
                }
            }
        }

        /// <summary>
        /// Mark a message as having received a reply.
        /// </summary>
        public static void MarkReplied(int messageId)
        {
            var msg = AllMessages.FirstOrDefault(m => m.Id == messageId);
            if (msg != null) msg.IsReplied = true;
        }

        /// <summary>
        /// Count unread messages for a kingdom.
        /// </summary>
        public static int CountUnread(string recipient)
        {
            return AllMessages.Count(m =>
                m.RecipientKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase) && !m.IsRead);
        }

        /// <summary>
        /// Count undelivered messages for a kingdom (unread AND not yet seen by AI).
        /// </summary>
        public static int CountUndelivered(string recipient)
        {
            return AllMessages.Count(m =>
                m.RecipientKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase) && !m.IsRead && !m.IsDelivered);
        }

        /// <summary>
        /// Build a formatted inbox string for the AI prompt.
        /// Shows unread messages + the last 3 messages of each active thread.
        /// </summary>
        public static string BuildInboxString(string recipient, int maxUnread = 5, int threadHistory = 2)
        {
            var unread = GetUnreadInbox(recipient);
            if (unread.Count == 0 && GetRecentThreads(recipient).Count == 0)
                return "";

            string result = "\n=== INCOMING DIPLOMATIC CORRESPONDENCE ===\n";

            // New unread mail
            if (unread.Count > 0)
            {
                result += $"[You have {unread.Count} UNREAD message(s)]\n";
                foreach (var m in unread.Take(maxUnread))
                {
                    result += $"  From: {m.SenderKingdom} | Re: {m.Subject}\n";
                    result += $"  \"{m.Body}\"\n";
                    if (m.OpinionShift != 0)
                        result += $"  [Diplomatic impact: {(m.OpinionShift > 0 ? "+" : "")}{m.OpinionShift:F0} opinion]\n";
                    result += "\n";
                }
            }

            // Recent thread history so the AI remembers the conversation
            var threads = GetRecentThreads(recipient);
            if (threads.Count > 0)
            {
                result += "--- Recent Conversation Threads ---\n";
                foreach (var pair in threads.Take(3))
                {
                    var msgs = GetThread(recipient, pair).TakeLast(threadHistory * 2);
                    if (msgs.Any())
                    {
                        result += $"  Thread with {pair}:\n";
                        foreach (var m in msgs)
                        {
                            string dir = m.SenderKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase) ? "You ->" : $"<- {m.SenderKingdom}";
                            result += $"    [{dir}] {m.Subject}: \"{m.Body}\"\n";
                        }
                    }
                }
            }

            result += "=== END CORRESPONDENCE ===\n";
            return result;
        }

        /// <summary>
        /// Get kingdoms that this kingdom has recently corresponded with.
        /// </summary>
        private static List<string> GetRecentThreads(string recipient)
        {
            var recent = AllMessages
                .Where(m => m.Timestamp > Time.time - THREAD_TTL)
                .Where(m => m.SenderKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase) ||
                            m.RecipientKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.SenderKingdom.Equals(recipient, StringComparison.OrdinalIgnoreCase) ? m.RecipientKingdom : m.SenderKingdom)
                .Distinct()
                .ToList();
            return recent;
        }

        /// <summary>
        /// Remove very old messages to prevent memory bloat.
        /// </summary>
        private static void CleanupOldMessages()
        {
            float cutoff = Time.time - MESSAGE_TTL;
            AllMessages.RemoveAll(m => m.Timestamp < cutoff);
            if (AllMessages.Count > 500)
            {
                // If still too many, keep only the 300 most recent
                AllMessages = AllMessages.OrderByDescending(m => m.Timestamp).Take(300).ToList();
            }
        }

        /// <summary>
        /// Apply a diplomatic opinion shift between two kingdoms.
        /// This uses the game's native diplomacy relation system.
        /// </summary>
        public static void ApplyOpinionShift(Kingdom sender, Kingdom recipient, float shift)
        {
            if (sender == null || recipient == null) return;
            var relation = World.world.diplomacy.getRelation(sender, recipient);
            if (relation == null) return;

            // We can't directly set opinion, but we can influence it by modifying the relation data
            // The game's opinion system is recalculated from OpinionAssets. However, we can log
            // the shift for our own tracking and show it to the AI.
            // For now, this is a no-op on native data but the shift is recorded in the message.
        }

        /// <summary>
        /// Reset all mail (e.g., on new game).
        /// </summary>
        public static void Reset()
        {
            AllMessages.Clear();
            _nextId = 1;
        }
    }
}
