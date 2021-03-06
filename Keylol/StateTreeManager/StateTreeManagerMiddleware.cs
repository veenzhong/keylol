﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Keylol.States;
using Keylol.Utilities;
using Microsoft.Owin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Keylol.StateTreeManager
{
    /// <summary>
    /// 状态树管理器中间件
    /// </summary>
    public class StateTreeManagerMiddleware : OwinMiddleware
    {
        private readonly PathString _basePath = new PathString("/states");
        private readonly Type _root = typeof(Root);

        /// <summary>
        /// 创建 <see cref="StateTreeManagerMiddleware"/>
        /// </summary>
        /// <param name="next">OWIN 管线的下一个中间件</param>
        public StateTreeManagerMiddleware(OwinMiddleware next) : base(next)
        {
        }

        /// <summary>Process an individual request.</summary>
        /// <param name="context"></param>
        public override async Task Invoke(IOwinContext context)
        {
            PathString treePathString;
            if (!context.Request.Path.StartsWithSegments(_basePath, out treePathString))
            {
                await Next.Invoke(context);
                return;
            }
            try
            {
                var result = await ProcessTreePath(treePathString, context);
                context.Response.ContentType = "application/json";
                await
                    context.Response.WriteAsync(result.GetType().IsPrimitive || result is string || result is decimal
                        ? result.ToString()
                        : JsonConvert.SerializeObject(result, new JsonSerializerSettings
                        {
                            Converters = new List<JsonConverter> {new StringEnumConverter(true)},
                            ContractResolver = new CamelCasePropertyNamesContractResolver(),
                            NullValueHandling = NullValueHandling.Ignore
                        }));
            }
            catch (NotAuthorizedException)
            {
                context.Response.ContentType = "text/plain";
                context.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Access denied.");
            }
        }

        private async Task<object> ProcessTreePath(PathString treePathString, IOwinContext owinContext)
        {
            var treePath = treePathString.HasValue ? treePathString.Value.Substring(1) : string.Empty;
            if (!string.IsNullOrWhiteSpace(treePath) && !treePath.EndsWith("/")) treePath = $"{treePath}/";

            var inLocator = false;
            var currentPropertyName = "[Root]";
            var currentType = _root;
            Type previousType = null;
            var nextPropertyNameBuilder = new StringBuilder();
            var nextPropertyHasLocator = false;
            var nextPropertyLocatorBuilder = new StringBuilder();
            var locators = new Dictionary<string, string>();
            var currentHasLocator = false;
            var authorized = true;

            Func<Type, Task<PropertyInfo>> commitNextProperty = async nowType =>
            {
                var nextPropertyName = nextPropertyNameBuilder.ToString()
                    .ToCase(NameConventionCase.DashedCase, NameConventionCase.PascalCase);
                nextPropertyNameBuilder.Clear();
                if (string.IsNullOrWhiteSpace(nextPropertyName))
                    return null;
                var nextProperty = nowType.GetProperty(nextPropertyName, BindingFlags.Instance | BindingFlags.Public);
                if (nextProperty == null)
                    throw new InvalidPropertyException(nextPropertyName);
                if (!authorized)
                {
                    if (nextProperty.GetCustomAttribute<AllowAnonymousAttribute>() != null)
                        authorized = true;
                    else
                        throw new NotAuthorizedException();
                }
                foreach (var authorizeAttribute in nextProperty.GetCustomAttributes<AuthorizeAttribute>(true))
                {
                    if (await authorizeAttribute.AuthorizeAsync(owinContext)) continue;
                    authorized = false;
                    break;
                }
                return nextProperty;
            };

            // Parse tree path
            foreach (var c in treePath)
            {
                switch (c)
                {
                    case '/':
                    {
                        if (inLocator)
                        {
                            nextPropertyLocatorBuilder.Append(c);
                        }
                        else if (nextPropertyHasLocator)
                        {
                            nextPropertyHasLocator = false;
                        }
                        else
                        {
                            var nextProperty = await commitNextProperty(currentType);
                            if (nextProperty == null)
                                throw new MalformedTreePathException();
                            previousType = currentType;
                            currentType = nextProperty.PropertyType;
                            currentPropertyName = nextProperty.Name;
                        }
                        break;
                    }

                    case '[':
                    {
                        if (inLocator)
                            throw new MalformedTreePathException();
                        inLocator = true;
                        nextPropertyHasLocator = true;
                        var nextProperty = await commitNextProperty(currentType);
                        if (nextProperty == null)
                            break;
                        previousType = currentType;
                        currentType = nextProperty.PropertyType;
                        currentPropertyName = nextProperty.Name;
                        break;
                    }

                    case ']':
                        if (!inLocator)
                            throw new MalformedTreePathException();
                        inLocator = false;
                        var locatorNameMethod = currentType.GetMethod("LocatorName",
                            BindingFlags.Static | BindingFlags.Public);
                        if (locatorNameMethod == null)
                            throw new LocatorNotSupportedException(currentPropertyName);
                        var locator = nextPropertyLocatorBuilder.ToString();
                        nextPropertyLocatorBuilder.Clear();
                        if (string.IsNullOrWhiteSpace(locator))
                            throw new EmptyLocatorException(currentPropertyName);
                        locators[locatorNameMethod.Invoke(null, null).ToString()] = locator;
                        currentHasLocator = true;
                        break;

                    default:
                        if (inLocator)
                        {
                            nextPropertyLocatorBuilder.Append(c);
                        }
                        else if (nextPropertyHasLocator)
                        {
                            throw new MalformedTreePathException();
                        }
                        else
                        {
                            nextPropertyNameBuilder.Append(c);
                            currentHasLocator = false;
                        }
                        break;
                }
            }

            if (inLocator || nextPropertyHasLocator ||
                nextPropertyNameBuilder.Length > 0 || nextPropertyLocatorBuilder.Length > 0)
                throw new MalformedTreePathException();

            if (!authorized)
                throw new NotAuthorizedException();

            var getMethod = previousType?.GetMethod($"Get{currentPropertyName}",
                BindingFlags.Public | BindingFlags.Static);
            if (getMethod != null)
                return await InvokeGetMethodAsync(getMethod, owinContext.Request.Query, locators);

            getMethod = currentType.GetMethod(currentHasLocator ? "Locate" : "Get",
                BindingFlags.Public | BindingFlags.Static);
            if (getMethod == null)
                throw new NoGetMethodException();
            return await InvokeGetMethodAsync(getMethod, owinContext.Request.Query, locators);
        }

        private async Task<object> InvokeGetMethodAsync(MethodInfo getMethod, IReadableStringCollection query,
            IReadOnlyDictionary<string, string> locators)
        {
            if (getMethod == null)
                throw new ArgumentNullException(nameof(getMethod));

            var parameters = getMethod.GetParameters();
            var arguments = new List<object>(parameters.Length);
            foreach (var parameter in parameters)
            {
                if (parameter.GetCustomAttribute<InjectedAttribute>() != null)
                {
                    arguments.Add(Global.Container.GetInstance(parameter.ParameterType));
                }
                else
                {
                    var value = query[parameter.Name] ??
                                query[
                                    parameter.Name.ToCase(NameConventionCase.CamelCase, NameConventionCase.SnakeCase)
                                    ];
                    if (value != null)
                    {
                        arguments.Add(CastType(value, parameter.ParameterType));
                    }
                    else if (locators.ContainsKey(parameter.Name))
                    {
                        arguments.Add(CastType(locators[parameter.Name], parameter.ParameterType));
                    }
                    else
                    {
                        if (parameter.HasDefaultValue)
                            arguments.Add(Type.Missing);
                        else
                            throw new ArgumentException("Missing parameter does not have a default value.",
                                parameter.Name);
                    }
                }
            }
            if (getMethod.ReturnType.IsGenericType &&
                getMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                dynamic task = getMethod.Invoke(null, arguments.ToArray());
                return await task;
            }
            return getMethod.Invoke(null, arguments.ToArray());
        }

        private static object CastType(string value, Type type)
        {
            return type.IsEnum ? Enum.Parse(type, value, true) : Convert.ChangeType(value, type);
        }
    }
}