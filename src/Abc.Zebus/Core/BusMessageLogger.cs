﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Abc.Zebus.Util.Annotations;
using Abc.Zebus.Util.Extensions;
using log4net;
using log4net.Core;

namespace Abc.Zebus.Core
{
    public class BusMessageLogger
    {
        private readonly Type _loggerType;
        private static readonly ConcurrentDictionary<Type, MessageTypeLogInfo> _logInfos = new ConcurrentDictionary<Type, MessageTypeLogInfo>();
        private readonly ILog _logger;

        private BusMessageLogger(Type loggerType)
        {
            _loggerType = loggerType;
            _logger = LogManager.GetLogger(loggerType);
        }

        public static BusMessageLogger Get<T>()
        {
            return Instance<T>.Value;
        }

        public bool IsLogEnabled(IMessage message)
        {
            var logInfo = _logInfos.GetOrAdd(message.GetType(), CreateLogger);
            return logInfo.Logger.IsInfoEnabled;
        }

        [StringFormatMethod("format")]
        public void LogFormat(string format, IMessage message, MessageId? messageId = null, int messageSize = 0, PeerId peerId = default(PeerId), Level logLevel = null)
        {
            var logInfo = _logInfos.GetOrAdd(message.GetType(), CreateLogger);
            if (!logInfo.Logger.IsInfoEnabled)
                return;

            var messageText = logInfo.GetMessageText(message);
            _logger.Logger.Log(_loggerType, logLevel ?? Level.Info, string.Format(format, messageText, messageId, messageSize, peerId), null);
        }

        [StringFormatMethod("format")]
        public void LogFormat(string format, IMessage message, MessageId messageId, int messageSize, IList<Peer> peers, Level logLevel = null)
        {
            if (peers.Count == 0)
            {
                LogFormat(format, message, messageId, messageSize);
                return;
            }
            if (peers.Count == 1)
            {
                LogFormat(format, message, messageId, messageSize, peerId: peers[0].Id);
                return;
            }

            var logInfo = _logInfos.GetOrAdd(message.GetType(), CreateLogger);
            if (!logInfo.Logger.IsInfoEnabled)
                return;

            var messageText = logInfo.GetMessageText(message);
            var otherPeersCount = peers.Count - 1;
            var peerIdText = otherPeersCount > 1
                ? peers[0].Id + " and " + otherPeersCount + " other peers"
                : peers[0].Id + " and " + otherPeersCount + " other peer";

            _logger.Logger.Log(_loggerType, logLevel ?? Level.Info, string.Format(format, messageText, messageId, messageSize, peerIdText), null);
        }

        public static string ToString(IMessage message)
        {
            var logInfo = _logInfos.GetOrAdd(message.GetType(), CreateLogger);
            return logInfo.GetMessageText(message);
        }

        private static MessageTypeLogInfo CreateLogger(Type messageType)
        {
            var logger = LogManager.GetLogger(messageType);
            var hasToStringOverride = HasToStringOverride(messageType);

            return new MessageTypeLogInfo(logger, hasToStringOverride, messageType.GetPrettyName());
        }

        private static bool HasToStringOverride(Type messageType)
        {
            var methodInfo = messageType.GetMethod("ToString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            return methodInfo != null;
        }

        private class MessageTypeLogInfo
        {
            public readonly ILog Logger;
            private readonly bool _hasToStringOverride;
            private readonly string _messageTypeName;

            public MessageTypeLogInfo(ILog logger, bool hasToStringOverride, string messageTypeName)
            {
                Logger = logger;
                _hasToStringOverride = hasToStringOverride;
                _messageTypeName = messageTypeName;
            }

            public string GetMessageText(IMessage message)
            {
                return _hasToStringOverride ? string.Format("{0} {{{1}}}", _messageTypeName, message) : string.Format("{0}", _messageTypeName);
            }
        }

        private static class Instance<T>
        {
            public static readonly BusMessageLogger Value = new BusMessageLogger(typeof(T));
        }
    }
}