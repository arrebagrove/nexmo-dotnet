﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Original source available at https://github.com/aspnet/Entropy/tree/dev/samples/Config.CustomConfigurationProviders.Sample

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Nexmo.Api.ConfigurationExtensions
{
    /// <summary>
    /// ConfigurationProvider for *.config files.  Only elements that contain
    /// &lt;add KeyName=&quot;A&quot; ValueName=&quot;B&quot;/&gt; or &lt;remove KeyName=&quot;A&quot; ValueName=&quot;B&quot;/&gt; (value is not
    /// considered for a remove action) as their descendents are used.
    /// All others are skipped.
    /// KeyName/ValueName can be configured. Default is &quot;key&quot; and &quot;value&quot;, respectively.
    /// </summary>
    /// <example>
    /// The following configuration file will result in the following key-value
    /// pairs in the dictionary:
    /// @{
    ///     { &quot;nodea:TheKey&quot; : &quot;TheValue&quot; },
    ///     { &quot;nodeb:nested:NestedKey&quot; : &quot;ValueA&quot; },
    ///     { &quot;nodeb:nested:NestedKey2&quot; : &quot;ValueB&quot; },
    /// }
    ///
    /// &lt;configuration&gt;
    ///     &lt;nodea&gt;
    ///         &lt;add key=&quot;TheKey&quot; value=&quot;TheValue&quot; /&gt;
    ///     &lt;/nodea&gt;
    ///     &lt;nodeb&gt;
    ///         &lt;nested&gt;
    ///             &lt;add key=&quot;NestedKey&quot; value=&quot;ValueA&quot; /&gt;
    ///             &lt;add key=&quot;NestedKey2&quot; value=&quot;ValueB&quot; /&gt;
    ///             &lt;remove key=&quot;SomeTestKey&quot; /&gt;
    ///         &lt;/nested&gt;
    ///     &lt;/nodeb&gt;
    /// &lt;/configuration&gt;
    /// 
    /// </example>
    public class KeyValueParser : IConfigurationParser
    {
        private readonly ILogger _logger;
        private readonly string _parentElementName = "appSettings";
        private readonly string _keyName = "key";
        private readonly string _valueName = "value";
        private readonly string[] _supportedActions = Enum.GetNames(typeof(ConfigurationAction)).Select(x => x.ToLowerInvariant()).ToArray();

        public KeyValueParser()
            : this("appSettings", "key", "value")
        { }

        public KeyValueParser(ILogger logger)
            : this("appSettings", "key", "value", logger)
        { }

        /// <summary>
        /// The key/value attribute names.
        /// </summary>
        public KeyValueParser(string parentElementName, string key, string value)
            : this(parentElementName, key, value, null)
        { }

        public KeyValueParser(string parentElementName, string key, string value, ILogger logger)
        {
            _parentElementName = parentElementName;
            _keyName = key;
            _valueName = value;
            _logger = logger;
        }

        public bool CanParseElement(XElement element)
        {
            var hasKeyAttribute = element.DescendantsAndSelf().Any(x => x.Attribute(_keyName) != null);

            return hasKeyAttribute;
        }

        public void ParseElement(XElement element, Stack<string> context, SortedDictionary<string, string> results)
        {
            if (!CanParseElement(element))
            {
                return;
            }

            if (!element.Elements().Any())
            {
                AddToDictionary(element, context, results);
            }

            context.Push(element.Name.ToString());

            foreach (var node in element.Elements())
            {
                var hasSupportedAction = node.DescendantsAndSelf().Any(x => _supportedActions.Contains(x.Name.ToString().ToLowerInvariant()));

                if (!hasSupportedAction)
                {
                    _logger?.LogWarning($"Contains an unsupported config element. [{node}]");

                    continue;
                }

                ParseElement(node, context, results);
            }

            context.Pop();
        }

        private void AddToDictionary(XElement element, Stack<string> context, SortedDictionary<string, string> results)
        {
            ConfigurationAction action;

            // ensure parent element matches
            if (element.Parent == null && !string.IsNullOrEmpty(_parentElementName))
            {
                _logger?.LogInformation($"No parent element, but expected parent element. [{element}]");

                return;
            }
            if (element.Parent != null && element.Parent.Name.ToString() != _parentElementName)
            {
                _logger?.LogInformation($"Matched element with an unsupported parent. [{element}]");

                return;
            }

            // matched, ensure we support action
            if (!Enum.TryParse(element.Name.ToString(), true, out action))
            {
                _logger?.LogInformation($"Element with an unsupported action. [{element}]");

                return;
            }

            var key = element.Attribute(_keyName);
            var value = element.Attribute(_valueName);

            if (key == null)
            {
                _logger?.LogInformation($"[{element}] is not supported because it does not have an attribute with {_keyName}");

                return;
            }

            var fullkey = GetKey(context, key.Value);

            switch (action)
            {
                case ConfigurationAction.Add:
                    string valueToAdd = null;

                    if (value == null)
                    {
                        _logger?.LogWarning($"Could not parse the value attribute [{_valueName}] from [{element}]. Using null as value...");
                    }
                    else
                    {
                        valueToAdd = value.Value;
                    }

                    if (results.ContainsKey(fullkey))
                    {
                        _logger?.LogWarning($"[{fullkey}] exists. Replacing existing value [{results[fullkey]}] with {valueToAdd}");

                        results[fullkey] = valueToAdd;
                    }
                    else
                    {
                        _logger?.LogInformation($"[{fullkey}] added, value = {valueToAdd}");
                        results.Add(fullkey, valueToAdd);
                    }
                    break;
                case ConfigurationAction.Remove:
                    _logger?.LogInformation($"[{fullkey}] removed");
                    results.Remove(fullkey);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported action: [{action}]");
            }
        }

        private static string GetKey(Stack<string> context, string name)
        {
            return string.Join(ConfigurationPath.KeyDelimiter, context.Reverse().Concat(new[] { name }));
        }
    }
}
