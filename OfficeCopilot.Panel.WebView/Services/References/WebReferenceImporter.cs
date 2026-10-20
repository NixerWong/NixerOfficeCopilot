using OfficeCopilot.Panel.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Panel.WebView.Services.References
{
    internal sealed class WebReferenceImporter
    {
        private static readonly object CertificateValidationSyncRoot = new object();
        private static readonly Regex TitleRegex = new Regex(@"<title[^>]*>(?<value>[\s\S]*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MetaCharsetRegex = new Regex(@"<meta[^>]+charset\s*=\s*['""]?(?<value>[^\s;'""><>/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MetaContentTypeCharsetRegex = new Regex(@"<meta[^>]+content\s*=\s*['""][^'""]*charset\s*=\s*(?<value>[^\s;'""><>/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HeadRegex = new Regex(@"<head\b[\s\S]*?</head>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BodyRegex = new Regex(@"<body\b[^>]*>(?<value>[\s\S]*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ScriptRegex = new Regex(@"<script\b[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StyleRegex = new Regex(@"<style\b[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NoScriptRegex = new Regex(@"<noscript\b[\s\S]*?</noscript>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SvgRegex = new Regex(@"<svg\b[\s\S]*?</svg>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FormRegex = new Regex(@"<form\b[\s\S]*?</form>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TemplateRegex = new Regex(@"<template\b[\s\S]*?</template>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CommentRegex = new Regex(@"<!--(?:(?!-->)[\s\S])*-->", RegexOptions.Compiled);
        private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiWhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public async Task<ReferenceItem> ImportAsync(string url, int timeoutSeconds, CancellationToken cancellationToken)
        {
            var normalizedUrl = NormalizeUrl(url);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
            {
                throw new InvalidOperationException("链接地址无效，请输入完整的 http:// 或 https:// 网址。");
            }

            var request = (HttpWebRequest)WebRequest.Create(normalizedUrl);
            request.Method = "GET";
            request.Accept = "text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.8";
            //request.UserAgent = "OfficeCopilot/1.0";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = Math.Max(1, timeoutSeconds) * 1000;
            request.ReadWriteTimeout = request.Timeout;

            var originalCertificateCallback = default(RemoteCertificateValidationCallback);
            lock (CertificateValidationSyncRoot)
            {
                originalCertificateCallback = ServicePointManager.ServerCertificateValidationCallback;
                ServicePointManager.ServerCertificateValidationCallback = TrustAllServerCertificates;
            }

            try
            {
                using (cancellationToken.Register(() => request.Abort(), false))
                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                using (var responseStream = response.GetResponseStream())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[] responseBytes;
                    using (var memoryStream = new MemoryStream())
                    {
                        if (responseStream != null)
                        {
                            await responseStream.CopyToAsync(memoryStream);
                        }

                        responseBytes = memoryStream.ToArray();
                    }

                    if (responseBytes == null || responseBytes.Length == 0)
                    {
                        throw new InvalidOperationException("网页内容为空。");
                    }

                    var encoding = ResolveEncoding(response, responseBytes);
                    var body = encoding.GetString(responseBytes);

                    var title = ExtractTitle(body);
                    var text = ExtractText(body);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidOperationException("未能提取网页正文内容。");
                    }

                    var uri = new Uri(normalizedUrl, UriKind.Absolute);
                    var summary = BuildSummary(title, uri.Host, text);

                    return new ReferenceItem
                    {
                        Kind = ReferenceItemKind.WebLink,
                        DisplayName = string.IsNullOrWhiteSpace(title) ? uri.Host : title,
                        MimeType = "text/html",
                        FileExtension = ".html",
                        InlineText = text,
                        Summary = summary,
                        Source = normalizedUrl,
                        Metadata =
                        {
                            ["url"] = normalizedUrl,
                            ["siteName"] = uri.Host,
                            ["fetchedAtUtc"] = DateTime.UtcNow.ToString("o"),
                            ["detectedEncoding"] = encoding.WebName
                        }
                    };
                }
            }
            finally
            {
                lock (CertificateValidationSyncRoot)
                {
                    ServicePointManager.ServerCertificateValidationCallback = originalCertificateCallback;
                }
            }
        }

        private static string NormalizeUrl(string url)
        {
            var value = (url ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && Uri.IsWellFormedUriString(value, UriKind.Absolute)
                && !string.IsNullOrWhiteSpace(uri.Host)
                && Uri.CheckHostName(uri.Host) != UriHostNameType.Unknown)
            {
                return uri.AbsoluteUri;
            }

            return string.Empty;
        }

        private static bool TrustAllServerCertificates(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        private static Encoding ResolveEncoding(HttpWebResponse response, byte[] responseBytes)
        {
            var bomEncoding = TryDetectBomEncoding(responseBytes);
            if (bomEncoding != null)
            {
                return bomEncoding;
            }

            var metaEncoding = TryResolveEncoding(ExtractCharsetFromBytes(responseBytes));
            if (metaEncoding != null)
            {
                return metaEncoding;
            }

            if (response != null)
            {
                var responseEncoding = TryResolveEncoding(response.CharacterSet);
                if (responseEncoding != null)
                {
                    return responseEncoding;
                }
            }

            return Encoding.UTF8;
        }

        private static Encoding TryDetectBomEncoding(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2)
            {
                return null;
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(true);
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode;
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode;
            }

            return null;
        }

        private static string ExtractCharsetFromBytes(byte[] responseBytes)
        {
            if (responseBytes == null || responseBytes.Length == 0)
            {
                return string.Empty;
            }

            var sampleLength = Math.Min(responseBytes.Length, 4096);
            var sample = Encoding.ASCII.GetString(responseBytes, 0, sampleLength);

            var match = MetaCharsetRegex.Match(sample);
            if (match.Success)
            {
                return match.Groups["value"].Value;
            }

            match = MetaContentTypeCharsetRegex.Match(sample);
            return match.Success ? match.Groups["value"].Value : string.Empty;
        }

        private static Encoding TryResolveEncoding(string charset)
        {
            var value = (charset ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (string.Equals(value, "gb2312", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "gbk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "x-gbk", StringComparison.OrdinalIgnoreCase))
            {
                value = "GB18030";
            }

            try
            {
                return Encoding.GetEncoding(value);
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractTitle(string html)
        {
            var match = TitleRegex.Match(html ?? string.Empty);
            if (!match.Success)
            {
                return string.Empty;
            }

            return NormalizeText(WebUtility.HtmlDecode(match.Groups["value"].Value));
        }

        private static string ExtractText(string html)
        {
            var value = html ?? string.Empty;
            value = HeadRegex.Replace(value, " ");

            var bodyMatch = BodyRegex.Match(value);
            if (bodyMatch.Success)
            {
                value = bodyMatch.Groups["value"].Value;
            }

            value = CommentRegex.Replace(value, " ");
            value = ScriptRegex.Replace(value, " ");
            value = StyleRegex.Replace(value, " ");
            value = NoScriptRegex.Replace(value, " ");
            value = SvgRegex.Replace(value, " ");
            value = FormRegex.Replace(value, " ");
            value = TemplateRegex.Replace(value, " ");
            value = value.Replace("</p>", "\n")
                .Replace("</div>", "\n")
                .Replace("<br>", "\n")
                .Replace("<br/>", "\n")
                .Replace("<br />", "\n")
                .Replace("</li>", "\n")
                .Replace("</h1>", "\n")
                .Replace("</h2>", "\n")
                .Replace("</h3>", "\n")
                .Replace("</h4>", "\n")
                .Replace("</h5>", "\n")
                .Replace("</h6>", "\n")
                .Replace("</tr>", "\n")
                .Replace("</table>", "\n");
            value = TagRegex.Replace(value, " ");
            value = WebUtility.HtmlDecode(value);
            value = NormalizeText(value);

            if (value.Length > 12000)
            {
                value = value.Substring(0, 12000).Trim() + "\n\n[内容已截断]";
            }

            return value;
        }

        private static string NormalizeText(string text)
        {
            var value = (text ?? string.Empty)
                .Replace('\u00a0', ' ')
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            var lines = value
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => MultiWhitespaceRegex.Replace(line, " ").Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            return string.Join("\n", lines).Trim();
        }

        private static string BuildSummary(string title, string host, string text)
        {
            var builder = new StringBuilder();
            builder.Append("网页参考：");
            builder.Append(string.IsNullOrWhiteSpace(title) ? host : title);

            if (!string.IsNullOrWhiteSpace(host))
            {
                builder.Append("，来源 ");
                builder.Append(host);
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append("，摘要 ");
                var preview = text.Length <= 120 ? text : text.Substring(0, 120).Trim() + "...";
                builder.Append(preview);
            }

            return builder.ToString();
        }
    }
}
