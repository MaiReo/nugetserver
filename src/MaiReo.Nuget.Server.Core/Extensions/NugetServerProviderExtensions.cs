﻿using MaiReo.Nuget.Server.Tools;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace MaiReo.Nuget.Server.Core
{
    [EditorBrowsable( EditorBrowsableState.Never )]
    public static class NugetServerProviderExtensions
    {

        public static void
        RedirectToCurrentApiVersion(
             this INugetServerProvider provider,
             HttpContext context,
             bool permanent = true )
        {
            var currentVersion = provider.GetApiMajorVersionUrl();
            var paths = context.Request.Path
                .ToString()
                .Split( new[] { '/' },
                    StringSplitOptions.RemoveEmptyEntries );
            string newPath = currentVersion;
            if (paths.Length > 0)
            {
                var otherPaths = paths.Skip( 1 );
                if (otherPaths.Any())
                {
                    newPath = newPath + "/" + string.Join( "/", otherPaths );
                }
            }
            if (context.Request.QueryString.HasValue)
            {
                newPath += context.Request.QueryString.Value;
            }
            context.Response.Redirect( newPath, permanent );
        }

        public static JsonSerializer CreateJsonSerializer(
            this INugetServerProvider provider )
        {
            return new JsonSerializer
            {
                NullValueHandling
                = NullValueHandling.Ignore,
                Formatting =
#if DEBUG
                Formatting.Indented,
#else
                provider
                .MvcJsonOptions
                .SerializerSettings.Formatting,
#endif
                ContractResolver
                = new CamelCasePropertyNamesContractResolver()
            };
        }

        public static void RespondNotFound(
            this INugetServerProvider provider,
            HttpContext context )
            => provider.RespondWithStatusCode(
                context, HttpStatusCode.NotFound );

        public static void RespondWithStatusCode(
            this INugetServerProvider provider,
            HttpContext context,
            HttpStatusCode statusCode )
            => context.Response.StatusCode
            = (int)statusCode;



        public static async Task WriteRawResponseAsync(
            this INugetServerProvider provider,
            HttpContext context, string contentType,
             byte[] raw, int bufferSize = 1024 )
        {
            if (raw == null)
            {
                provider.RespondNotFound( context );
                return;
            }
            context.Response.StatusCode
                = (int)HttpStatusCode.OK;
            context.Response.ContentLength = raw.Length;
            context.Response.ContentType = contentType;

            if (HttpMethods.IsHead( context.Request.Method ))
            {
                return;
            }

            if (HttpMethods.IsGet( context.Request.Method ))
            {
                using (var ms = new MemoryStream( raw ))
                {
                    await ms.CopyToAsync( context.Response.Body,
                        bufferSize, context.RequestAborted );
                }
            }
        }

        public static async Task WriteJsonResponseAsync<T>(
            this INugetServerProvider provider,
            HttpContext context, T value,
            JsonSerializer serializer = null,
            bool useGzip = false )
            where T : class
        {
            if (value == null)
            {
                provider.RespondNotFound( context );
                return;
            }
            serializer = serializer ?? CreateJsonSerializer( provider );

            var sb = new StringBuilder();
            using (var sw = new StringWriter( sb ))
            {
                serializer.Serialize( sw, value );
            }

            context.Response.StatusCode
                = (int)HttpStatusCode.OK;

            using (var content = CreateHttpContent(
                sb, useGzip: useGzip ))
            {
                context.Response.ContentLength
                       = content.Headers.ContentLength;
                context.Response.ContentType
                    = content.Headers.ContentType.ToString();
                if (content.Headers.ContentEncoding.Any())
                {
                    context.Response.Headers["Content-Encoding"] =
                        content.Headers.ContentEncoding.ToArray();
                }

                if (HttpMethods.IsHead( context.Request.Method ))
                {
                    return;
                }

                if (HttpMethods.IsGet( context.Request.Method ))
                {
                    await Task.Run( async () =>
                         await content.CopyToAsync(
                             context.Response.Body ),
                            context.RequestAborted );
                }

            }
        }

        private static HttpContent CreateHttpContent(
            StringBuilder stringBuilder,
            string contentType = "application/json",
            bool useGzip = false )
        {
            var encoding = Encoding.UTF8;
            if (useGzip)
            {
                var content = new ByteArrayContent( Gzip.Write( stringBuilder, encoding ) );
                content.Headers.ContentEncoding.Clear();
                content.Headers.ContentEncoding.Add( "gzip" );
                content.Headers.ContentType = MediaTypeHeaderValue.Parse( contentType );
                content.Headers.ContentType.CharSet = "utf-8";
                return content;
            }
            else
            {
                return new StringContent( stringBuilder.ToString(), encoding, contentType );
            }
        }

        public static PathString GetApiMajorVersionUrl(
            this INugetServerProvider provider )
        {
            var majorVersion = provider
                ?.NugetServerOptions
                ?.ApiVersion?.Major;

            if (!majorVersion.HasValue)
            {
                throw new InvalidOperationException(
                    "Nuget server api version not specified." );
            }
            return "/v" + majorVersion;

        }
        public static PathString GetResourceValue(
            this INugetServerProvider provider,
            NugetServerResourceType resourceType )
            =>
            provider
            ?.NugetServerOptions
            ?.Resources
            ?.ContainsKey( resourceType ) != true
            ? null
            : provider
            .NugetServerOptions
            .Resources[resourceType];


        public static PathString GetResourceUrlPath(
            this INugetServerProvider provider,
            NugetServerResourceType resourceType )
        {
            var majorVersion = provider
                .GetApiMajorVersionUrl();

            var path = provider.GetResourceValue(
                    resourceType );
            if (!path.HasValue)
            {
                throw new InvalidOperationException(
                    "Nuget server resource " +
                    resourceType +
                    " not specified." );
            }
            return majorVersion + path;
        }


        public static IEnumerable<PathString> GetResourceUrlPaths(
            this INugetServerProvider provider,
            params NugetServerResourceType[] resourceTypes ) =>
            resourceTypes
            ?.Select( t => GetResourceUrlPath( provider, t ) )
            ?? Enumerable.Empty<PathString>();

        public static bool IsMatchVerbs(
            this INugetServerProvider provider,
            HttpContext context,
            params Func<string, bool>[] verbs )
            => verbs?.Any(
                v => v?.Invoke( context.Request.Method ) == true ) == true;

        public static bool IsMatchResources(
            this INugetServerProvider provider,
            HttpContext context,
            params NugetServerResourceType[] resourceTypes )
            => context
                .IsRequestingUrls( provider
                .GetResourceUrlPaths( resourceTypes ) );

        public static bool IsMatchResource(
            this INugetServerProvider provider,
            HttpContext context,
            NugetServerResourceType resourceType )
            => context
                .IsRequestingUrl( provider
                .GetResourceUrlPath( resourceType ) );

        public static bool IsMatchResourceIgnoreApiVersion(
            this INugetServerProvider provider,
            HttpContext context,
            NugetServerResourceType resourceType )
        {
            var currentPath = provider
                   .GetResourceUrlPath( resourceType );
            var directPath = provider
                   .GetResourceValue( resourceType );
            var v2Path = ((PathString)"/v2").Add( directPath );
            var apiV2Path = ((PathString)"/api").Add( v2Path );
            return context
                .IsRequestingUrls(
                  new[]
                  {
                      currentPath,
                      directPath,
                      v2Path,
                      apiV2Path
                  } );
        }


        public static bool IsMatchExtensionName(
            this INugetServerProvider provider,
            HttpContext context, string extensionName )
            => context
                .IsRequestingWithExtensionName( extensionName );

        public static bool IsMatchPath(
            this INugetServerProvider provider,
            HttpContext context, PathString path )
            => context.IsRequestingUrl( path );
    }
}
