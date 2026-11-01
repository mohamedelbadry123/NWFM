using System.Globalization;
using System.Net;

namespace Platform.Application.Signup.Common;

public sealed record SignupEmail(string Subject, string HtmlBody);

/// <summary>
/// The signup e-mails, in the language the customer picked. Every interpolated value is HTML-encoded:
/// organisation and person names come straight from an anonymous form.
/// </summary>
public static class SignupEmails
{
    public const string SenderName = "NWFM";

    public static SignupEmail Verification(string language, string organizationName, string link, int validHours)
    {
        var org = WebUtility.HtmlEncode(organizationName);
        var href = WebUtility.HtmlEncode(link);
        var hours = validHours.ToString(CultureInfo.InvariantCulture);

        return IsArabic(language)
            ? new SignupEmail(
                "أكّد بريدك الإلكتروني لإنشاء مساحة عمل NWFM",
                Wrap(language,
                    $"<p>شكرًا لتسجيل <strong>{org}</strong> في NWFM.</p>" +
                    $"<p>اضغط على الرابط التالي لتأكيد بريدك الإلكتروني وبدء إنشاء مساحة العمل:</p>" +
                    $"<p><a href=\"{href}\">تأكيد البريد الإلكتروني</a></p>" +
                    $"<p>الرابط صالح لمدة {hours} ساعة. إذا لم تطلب ذلك فتجاهل هذه الرسالة.</p>"))
            : new SignupEmail(
                "Confirm your e-mail to create your NWFM workspace",
                Wrap(language,
                    $"<p>Thank you for signing <strong>{org}</strong> up for NWFM.</p>" +
                    $"<p>Open the link below to confirm your e-mail address and start creating the workspace:</p>" +
                    $"<p><a href=\"{href}\">Confirm e-mail address</a></p>" +
                    $"<p>The link is valid for {hours} hours. If you did not ask for this, ignore this message.</p>"));
    }

    /// <summary>Sent once the e-mail is confirmed, when the request waits for staff approval.</summary>
    public static SignupEmail AwaitingApproval(string language, string organizationName)
    {
        var org = WebUtility.HtmlEncode(organizationName);

        return IsArabic(language)
            ? new SignupEmail(
                "تم استلام طلب مساحة عمل NWFM",
                Wrap(language,
                    $"<p>شكرًا لتأكيد بريدك الإلكتروني. استلمنا طلب إنشاء مساحة العمل <strong>{org}</strong>.</p>" +
                    "<p>سيراجع فريقنا الطلب، وستصلك رسالة تحتوي على بيانات الدخول فور الموافقة عليه.</p>"))
            : new SignupEmail(
                "We received your NWFM workspace request",
                Wrap(language,
                    $"<p>Thank you for confirming your e-mail. We received the request for the workspace <strong>{org}</strong>.</p>" +
                    "<p>Our team will review it, and you will receive your sign-in details as soon as it is approved.</p>"));
    }

    /// <summary>Sent when staff reject the request. The staff reason is internal and never included.</summary>
    public static SignupEmail Rejected(string language, string organizationName)
    {
        var org = WebUtility.HtmlEncode(organizationName);

        return IsArabic(language)
            ? new SignupEmail(
                "بخصوص طلب مساحة عمل NWFM",
                Wrap(language,
                    $"<p>نأسف لإبلاغك بأنه تعذّر قبول طلب إنشاء مساحة العمل <strong>{org}</strong>.</p>" +
                    "<p>إذا كانت لديك أي استفسارات فيُرجى التواصل مع فريقنا.</p>"))
            : new SignupEmail(
                "About your NWFM workspace request",
                Wrap(language,
                    $"<p>We are sorry, but we could not accept the request for the workspace <strong>{org}</strong>.</p>" +
                    "<p>If you have any questions, please get in touch with our team.</p>"));
    }

    /// <summary>The new workspace's first administrator: where to sign in and a one-time set-password link.</summary>
    public static SignupEmail Activation(string language, string organizationName, string workspaceUrl, string userName,
        string link, int validHours)
    {
        var org = WebUtility.HtmlEncode(organizationName);

        return IsArabic(language)
            ? new SignupEmail(
                "مساحة عمل NWFM جاهزة",
                Wrap(language,
                    $"<p>تم إنشاء مساحة العمل <strong>{org}</strong>.</p>" +
                    Credentials(language, workspaceUrl, userName, link, validHours)))
            : new SignupEmail(
                "Your NWFM workspace is ready",
                Wrap(language,
                    $"<p>The workspace <strong>{org}</strong> has been created.</p>" +
                    Credentials(language, workspaceUrl, userName, link, validHours)));
    }

    /// <summary>An administrator staff added to an existing workspace, or whose sign-in link was sent again.</summary>
    public static SignupEmail AdministratorCredentials(string language, string organizationName, string workspaceUrl,
        string userName, string link, int validHours)
    {
        var org = WebUtility.HtmlEncode(organizationName);

        return IsArabic(language)
            ? new SignupEmail(
                "بيانات الدخول إلى مساحة عمل NWFM",
                Wrap(language,
                    $"<p>تمت إضافتك كمسؤول في مساحة العمل <strong>{org}</strong>.</p>" +
                    Credentials(language, workspaceUrl, userName, link, validHours)))
            : new SignupEmail(
                "Your NWFM sign-in details",
                Wrap(language,
                    $"<p>You are an administrator of the workspace <strong>{org}</strong>.</p>" +
                    Credentials(language, workspaceUrl, userName, link, validHours)));
    }

    // No password travels by e-mail: the account has none until the link sets one.
    private static string Credentials(string language, string workspaceUrl, string userName, string link, int validHours)
    {
        var url = WebUtility.HtmlEncode(workspaceUrl);
        var user = WebUtility.HtmlEncode(userName);
        var href = WebUtility.HtmlEncode(link);
        var hours = validHours.ToString(CultureInfo.InvariantCulture);

        return IsArabic(language)
            ? $"<p>العنوان: <a href=\"{url}\">{url}</a><br/>اسم المستخدم: <strong>{user}</strong></p>" +
              "<p>اضغط على الرابط التالي لتعيين كلمة المرور ثم سجّل الدخول:</p>" +
              $"<p><a href=\"{href}\">تعيين كلمة المرور</a></p>" +
              $"<p>الرابط صالح لمدة {hours} ساعة ويُستخدم مرة واحدة.</p>"
            : $"<p>Address: <a href=\"{url}\">{url}</a><br/>User name: <strong>{user}</strong></p>" +
              "<p>Open the link below to set your password, then sign in:</p>" +
              $"<p><a href=\"{href}\">Set password</a></p>" +
              $"<p>The link is valid for {hours} hours and works once.</p>";
    }

    private static bool IsArabic(string language) =>
        string.Equals(language, TenantSettings.Languages.Arabic, StringComparison.OrdinalIgnoreCase);

    private static string Wrap(string language, string content) =>
        $"<div dir=\"{(IsArabic(language) ? "rtl" : "ltr")}\" style=\"font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:14px\">{content}</div>";
}
