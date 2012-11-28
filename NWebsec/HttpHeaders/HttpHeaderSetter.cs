﻿// Copyright (c) André N. Klingsheim. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Web;
using NWebsec.Modules.Configuration;
using NWebsec.Modules.Configuration.Csp;

namespace NWebsec.HttpHeaders
{
    class HttpHeaderSetter
    {
        private readonly HttpContextBase context;
        private readonly HttpResponseBase response;
        private readonly HttpRequestBase request;
        internal HttpHeaderSetter(HttpContextBase context)
        {
            this.context = context;
            request = context.Request;
            response = context.Response;
        }

        public void SetNoCacheHeaders(SimpleBooleanConfigurationElement getNoCacheHeadersWithOverride)
        {
            if (!getNoCacheHeadersWithOverride.Enabled)
                return;

            if (context.CurrentHandler == null)
                return;

            var handlerType = context.CurrentHandler.GetType();
            if (handlerType.FullName.Equals("System.Web.Optimization.BundleHandler"))
                return;

            Debug.Assert(request.Url != null, "request.Url != null");
            var path = request.Url.AbsolutePath;
            if (path.EndsWith("ScriptResource.axd") || path.EndsWith("WebResource.axd"))
                return;

            response.Cache.SetCacheability(HttpCacheability.NoCache);
            response.Cache.SetNoStore();
            response.Cache.SetRevalidation(HttpCacheRevalidation.AllCaches);

            response.AddHeader("Pragma", "no-cache");
        }

        internal void AddXFrameoptionsHeader(XFrameOptionsConfigurationElement xFrameOptionsConfig)
        {

            string frameOptions;
            switch (xFrameOptionsConfig.Policy)
            {
                case HttpHeadersConstants.XFrameOptions.Disabled:
                    return;

                case HttpHeadersConstants.XFrameOptions.Deny:
                    frameOptions = "Deny";
                    break;

                case HttpHeadersConstants.XFrameOptions.SameOrigin:
                    frameOptions = "SameOrigin";
                    break;

                //case HttpHeadersConstants.XFrameOptions.AllowFrom:
                //    frameOptions = "ALLOW-FROM " + headerConfig.SecurityHttpHeaders.XFrameOptions.Origin.GetLeftPart(UriPartial.Authority);
                //    break;

                default:
                    throw new NotImplementedException("Apparently someone forgot to implement support for: " + xFrameOptionsConfig.Policy);

            }
            response.AddHeader(HttpHeadersConstants.XFrameOptionsHeader, frameOptions);
        }

        internal void AddHstsHeader(HstsConfigurationElement hstsConfig)
        {

            var seconds = (int)hstsConfig.MaxAge.TotalSeconds;

            if (seconds == 0) return;

            var includeSubdomains = (hstsConfig.IncludeSubdomains ? "; includeSubDomains" : "");
            var value = String.Format("max-age={0}{1}", seconds, includeSubdomains);

            response.AddHeader(HttpHeadersConstants.StrictTransportSecurityHeader, value);
        }

        internal void AddXContentTypeOptionsHeader(SimpleBooleanConfigurationElement xContentTypeOptionsConfig)
        {
            if (xContentTypeOptionsConfig.Enabled)
            {
                response.AddHeader(HttpHeadersConstants.XContentTypeOptionsHeader, "nosniff");
            }
        }

        internal void AddXDownloadOptionsHeader(SimpleBooleanConfigurationElement xDownloadOptionsConfig)
        {
            if (xDownloadOptionsConfig.Enabled)
            {
                response.AddHeader(HttpHeadersConstants.XDownloadOptionsHeader, "noopen");
            }
        }

        internal void AddXXssProtectionHeader(XXssProtectionConfigurationElement xXssProtectionConfig)
        {
            string value;
            switch (xXssProtectionConfig.Policy)
            {
                case HttpHeadersConstants.XXssProtection.Disabled:
                    return;
                case HttpHeadersConstants.XXssProtection.FilterDisabled:
                    value = "0";
                    break;

                case HttpHeadersConstants.XXssProtection.FilterEnabled:
                    value = (xXssProtectionConfig.BlockMode ? "1; mode=block" : "1");
                    break;

                default:
                    throw new NotImplementedException("Somebody apparently forgot to implement support for: " + xXssProtectionConfig.Policy);

            }

            response.AddHeader(HttpHeadersConstants.XXssProtectionHeader, value);
        }

        internal void AddXCspHeaders(CspConfigurationElement cspConfig, bool reportOnly)
        {
            if (!cspConfig.Enabled) return;

            var headerValue = CreateCspHeaderValue(cspConfig);
            var headerName = (reportOnly
                                          ? HttpHeadersConstants.ContentSecurityPolicyReportOnlyHeader
                                          : HttpHeadersConstants.ContentSecurityPolicyHeader);

            response.AddHeader(headerName, headerValue);

            if (cspConfig.XContentSecurityPolicyHeader)
            {
                headerName = (reportOnly
                                  ? HttpHeadersConstants.XContentSecurityPolicyReportOnlyHeader
                                  : HttpHeadersConstants.XContentSecurityPolicyHeader);

                response.AddHeader(headerName, headerValue);
            }

            if (cspConfig.XWebKitCspHeader)
            {
                headerName = (reportOnly
                                  ? HttpHeadersConstants.XWebKitCspReportOnlyHeader
                                  : HttpHeadersConstants.XWebKitCspHeader);

                response.AddHeader(headerName, headerValue);
            }
        }

        internal void SuppressVersionHeaders(SuppressVersionHeadersConfigurationElement suppressVersionHeadersConfig)
        {
            if (!suppressVersionHeadersConfig.Enabled) return;

            foreach (var header in HttpHeadersConstants.VersionHeaders)
            {
                response.Headers.Remove(header);
            }
            var serverName = (String.IsNullOrEmpty(suppressVersionHeadersConfig.ServerHeader)
                                  ? "Webserver 1.0"
                                  : suppressVersionHeadersConfig.ServerHeader);
            response.Headers.Set("Server", serverName);
        }

        private string CreateCspHeaderValue(CspConfigurationElement config)
        {
            var sb = new StringBuilder();

            sb.Append(CreateDirectiveValue("default-src", GetDirectiveList(config.DefaultSrc)));
            sb.Append(CreateDirectiveValue("script-src", GetDirectiveList(config.ScriptSrc)));
            sb.Append(CreateDirectiveValue("object-src", GetDirectiveList(config.ObjectSrc)));
            sb.Append(CreateDirectiveValue("style-src", GetDirectiveList(config.StyleSrc)));
            sb.Append(CreateDirectiveValue("img-src", GetDirectiveList(config.ImgSrc)));
            sb.Append(CreateDirectiveValue("media-src", GetDirectiveList(config.MediaSrc)));
            sb.Append(CreateDirectiveValue("frame-src", GetDirectiveList(config.FrameSrc)));
            sb.Append(CreateDirectiveValue("font-src", GetDirectiveList(config.FontSrc)));
            sb.Append(CreateDirectiveValue("connect-src", GetDirectiveList(config.ConnectSrc)));
            sb.Append(CreateDirectiveValue("report-uri", GetReportUriList(config.ReportUriDirective)));

            return sb.ToString().TrimEnd(new[] {' ', ';'});
        }

        private ICollection<string> GetDirectiveList(CspDirectiveBaseConfigurationElement directive)
        {
            var sources = new LinkedList<string>();

            if (directive.None)
                sources.AddLast("'none'");

            if (directive.Self)
                sources.AddLast("'self'");

            var allowUnsafeInlineElement = directive as CspDirectiveUnsafeInlineConfigurationElement;
            if (allowUnsafeInlineElement != null && allowUnsafeInlineElement.UnsafeInline)
                sources.AddLast("'unsafe-inline'");

            var allowUnsafeEvallement = directive as CspDirectiveUnsafeInlineUnsafeEvalConfigurationElement;
            if (allowUnsafeEvallement != null && allowUnsafeEvallement.UnsafeEval)
                sources.AddLast("'unsafe-eval'");

            if (!string.IsNullOrEmpty(directive.Source))
                sources.AddLast(directive.Source);

            foreach (CspSourceConfigurationElement sourceElement in directive.Sources)
            {
                sources.AddLast(sourceElement.Source);
            }
            return sources;
        }

        private ICollection<string> GetReportUriList(CspReportUriDirectiveConfigurationElement directive)
        {
            var reportUris = new LinkedList<string>();
            if (!String.IsNullOrEmpty(directive.ReportUri))
                reportUris.AddLast(directive.ReportUri);

            foreach (CspReportUriConfigurationElement reportUri in directive.ReportUris)
            {
                reportUris.AddLast(reportUri.ReportUri.ToString());
            }
            return reportUris;
        }

        private string CreateDirectiveValue(string directiveName, ICollection<string> sources)
        {
            if (sources.Count < 1) return String.Empty;
            var sb = new StringBuilder();
            sb.Append(directiveName);
            sb.Append(' ');
            foreach (var source in sources)
            {
                sb.Append(source);
                sb.Append(' ');
            }
            sb.Insert(sb.Length - 1, ';');
            return sb.ToString();
        }

    }
}
