using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FlapGame.GameLib.Iap
{
    /// <summary>Transactions already granted, so a purchase the store delivers again (not confirmed before the app closed) is not granted twice.</summary>
    public interface IIapTransactionLog
    {
        bool Contains(string transactionId);

        void Add(string transactionId);
    }

    /// <summary>The last <see cref="Capacity"/> granted transaction ids in PlayerPrefs.</summary>
    public sealed class PlayerPrefsTransactionLog : IIapTransactionLog
    {
        public const string Key = "GameLib.Iap.GrantedTransactions";
        public const int Capacity = 100;
        private const char Separator = '\n';

        private List<string> ids;

        public bool Contains(string transactionId) => !string.IsNullOrEmpty(transactionId) && Load().Contains(transactionId);

        public void Add(string transactionId) => Add(transactionId, save: true);

        /// <param name="save">False only when a <c>PlayerPrefs.Save</c> follows before the store confirmation (it writes
        /// every key, this one too).</param>
        public void Add(string transactionId, bool save)
        {
            if (string.IsNullOrEmpty(transactionId) || Contains(transactionId)) return;
            List<string> list = Load();
            list.Add(transactionId);
            if (list.Count > Capacity) list.RemoveRange(0, list.Count - Capacity);
            PlayerPrefs.SetString(Key, string.Join(Separator, list));
            if (save) PlayerPrefs.Save(); // the grant must survive a crash before the store confirmation
        }

        private List<string> Load() =>
            ids ??= PlayerPrefs.GetString(Key, string.Empty).Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>In-memory log (tests, or games that persist grants elsewhere).</summary>
    public sealed class MemoryTransactionLog : IIapTransactionLog
    {
        private readonly HashSet<string> ids = new(StringComparer.Ordinal);

        public bool Contains(string transactionId) => transactionId != null && ids.Contains(transactionId);

        public void Add(string transactionId)
        {
            if (!string.IsNullOrEmpty(transactionId)) ids.Add(transactionId);
        }
    }
}
