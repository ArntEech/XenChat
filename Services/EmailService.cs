using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace XenChat.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendOtpAsync(string toEmail, string otp)
        {
            _logger.LogInformation("[EmailService] Verification code for {Email} is: {Otp}", toEmail, otp);
            Console.WriteLine($"[EmailService] Verification code for {toEmail} is: {otp}");

            var host = !string.IsNullOrWhiteSpace(_config["EmailSettings:SmtpHost"]) ? _config["EmailSettings:SmtpHost"] : (Environment.GetEnvironmentVariable("EmailSettings__SmtpHost") ?? "smtp.gmail.com");
            var portStr = !string.IsNullOrWhiteSpace(_config["EmailSettings:SmtpPort"]) ? _config["EmailSettings:SmtpPort"] : Environment.GetEnvironmentVariable("EmailSettings__SmtpPort");
            var port = string.IsNullOrEmpty(portStr) ? 587 : int.Parse(portStr!);
            var senderName = !string.IsNullOrWhiteSpace(_config["EmailSettings:SenderName"]) ? _config["EmailSettings:SenderName"] : (Environment.GetEnvironmentVariable("EmailSettings__SenderName") ?? "XenChat");
            var senderEmail = !string.IsNullOrWhiteSpace(_config["EmailSettings:SenderEmail"]) ? _config["EmailSettings:SenderEmail"] : (Environment.GetEnvironmentVariable("EmailSettings__SenderEmail") ?? "");
            var senderPassword = !string.IsNullOrWhiteSpace(_config["EmailSettings:SenderPassword"]) ? _config["EmailSettings:SenderPassword"] : (Environment.GetEnvironmentVariable("EmailSettings__SenderPassword") ?? "");

            if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPassword))
            {
                _logger.LogWarning("[EmailService] SMTP credentials not fully configured. Code logged above.");
                return;
            }

            try
            {
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
                // Set reasonable timeout
                client.Timeout = 10000;
                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(senderEmail, senderPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                _logger.LogInformation("[EmailService] Email sent successfully to {Email}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmailService] Failed to send email via SMTP to {Email}. Verification code was: {Otp}", toEmail, otp);
                // Do not throw in development if SMTP fails - allow testing to continue with logged OTP
                if (_config.GetValue<bool>("EmailSettings:ThrowOnFailure", false))
                {
                    throw;
                }
            }
        }
    }
}