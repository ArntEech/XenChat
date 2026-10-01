using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace XenChat.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendOtpAsync(string toEmail, string otp)
        {
            var host = _config["EmailSettings:SmtpHost"];
            var port = int.Parse(_config["EmailSettings:SmtpPort"]);
            var senderName = _config["EmailSettings:SenderName"];
            var senderEmail = _config["EmailSettings:SenderEmail"];
            var senderPassword = _config["EmailSettings:SenderPassword"];

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = "Your XenChat verification code";

            message.Body = new TextPart("html")
            {
                Text = $@"
                    <div style='font-family:Arial,sans-serif;background:#313337;color:#fff;padding:40px;text-align:center;'>
                        <h1 style='color:#ee4c49;margin-bottom:24px;'>XenChat</h1>
                        <p style='font-size:16px;'>Your verification code is:</p>
                        <div style='font-size:36px;letter-spacing:8px;font-weight:bold;color:#ee4c49;margin:24px 0;'>{otp}</div>
                        <p style='font-size:13px;color:#b5b5b5;'>This code expires in 5 minutes.</p>
                    </div>"
            };

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(senderEmail, senderPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}