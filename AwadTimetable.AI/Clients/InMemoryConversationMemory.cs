using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwadTimetable.AI.Interfaces;

namespace AwadTimetable.AI.Clients;

/// <summary>
/// ذاكرة محادثة في الذاكرة (In-Memory)
/// </summary>
public sealed class InMemoryConversationMemory : IConversationMemory
{
    private readonly object _lock = new();
    private readonly List<ChatMessage> _messages = new();
    private const int MaxStoredMessages = 200;

    public Task AddAsync(string userMessage, string assistantResponse)
    {
        lock (_lock)
        {
            _messages.Add(new ChatMessage { Role = "user", Content = userMessage });
            _messages.Add(new ChatMessage { Role = "assistant", Content = assistantResponse });

            if (_messages.Count > MaxStoredMessages)
            {
                _messages.RemoveRange(0, _messages.Count - MaxStoredMessages);
            }
        }
        return Task.CompletedTask;
    }

    public Task<List<ChatMessage>> GetContextAsync(int maxMessages = 20)
    {
        lock (_lock)
        {
            var take = Math.Min(maxMessages, _messages.Count);
            return Task.FromResult(_messages.Skip(_messages.Count - take).ToList());
        }
    }

    public Task ClearAsync()
    {
        lock (_lock)
        {
            _messages.Clear();
        }
        return Task.CompletedTask;
    }

    public Task<string> BuildContextAsync(string currentQuery, int maxTokens = 4000)
    {
        lock (_lock)
        {
            // تقدير تقريبي: 4 أحرف لكل رمز
            var maxChars = maxTokens * 4;
            var parts = new List<string>();
            var used = 0;

            for (var i = _messages.Count - 1; i >= 0; i--)
            {
                var text = $"{_messages[i].Role}: {_messages[i].Content}";
                if (used + text.Length > maxChars) break;
                parts.Insert(0, text);
                used += text.Length;
            }

            parts.Add($"user: {currentQuery}");
            return Task.FromResult(string.Join("\n", parts));
        }
    }
}
