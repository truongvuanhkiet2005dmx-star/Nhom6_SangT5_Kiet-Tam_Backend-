using FirebaseAdmin.Messaging;

namespace SmartMoney.Api.Services;

public class PushNotificationService
{
    private readonly FirebaseMessaging? _messaging;

    public PushNotificationService(FirebaseMessaging? messaging)
    {
        _messaging = messaging;
    }

    public bool IsConfigured => _messaging != null;

    public async Task<string?> SendAsync(
        string token,
        string title,
        string body)
    {
        if (_messaging == null)
            return null;

        var message = new Message
        {
            Token = token,
            Notification = new Notification
            {
                Title = title,
                Body = body
            }
        };

        return await _messaging.SendAsync(message);
    }
}