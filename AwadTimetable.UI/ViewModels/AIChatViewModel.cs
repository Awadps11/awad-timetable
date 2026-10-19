using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AwadTimetable.AI.Interfaces;
using AwadTimetable.AI.Tools;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض المحادثة الذكية (الصفحة الكاملة)
/// </summary>
public partial class AIChatViewModel : ObservableObject
{
    private readonly ISchedulingAgent _schedulingAgent;

    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messages = new();

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    public AIChatViewModel(ISchedulingAgent schedulingAgent)
    {
        _schedulingAgent = schedulingAgent;

        // رسالة ترحيب
        Messages.Add(new ChatMessageViewModel
        {
            Role = "assistant",
            Content = "مرحباً! أنا مساعدك الذكي للجداول المدرسية. كيف يمكنني مساعدتك اليوم؟",
            Timestamp = DateTime.Now,
            IsFromUser = false
        });
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsProcessing) return;

        var userMessage = InputText;
        InputText = string.Empty;
        IsProcessing = true;

        var userMsg = new ChatMessageViewModel
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTime.Now,
            IsFromUser = true
        };
        Messages.Add(userMsg);

        try
        {
            var response = await _schedulingAgent.ProcessAsync(userMessage);
            
            var assistantMsg = new ChatMessageViewModel
            {
                Role = "assistant",
                Content = response.Message,
                Timestamp = DateTime.Now,
                IsFromUser = false
            };
            Messages.Add(assistantMsg);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageViewModel
            {
                Role = "assistant",
                Content = $"عذراً، حدث خطأ: {ex.Message}",
                Timestamp = DateTime.Now,
                IsFromUser = false,
                IsError = true
            });
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        Messages.Add(new ChatMessageViewModel
        {
            Role = "assistant",
            Content = "تم مسح المحادثة. كيف يمكنني مساعدتك؟",
            Timestamp = DateTime.Now,
            IsFromUser = false
        });
    }
}
