﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.OpenApi.Any;
using SharpYaml.Tokens;

namespace Microsoft.PowerFx.Connectors
{
    internal class SwaggerJsonSchema : SwaggerJsonExtensions, ISwaggerSchema
    {
        private readonly JsonElement _schema;

        public static ISwaggerSchema New(JsonElement schema)
        {
            return schema.ValueKind != JsonValueKind.Object ? null : new SwaggerJsonSchema(schema);
        }

        private SwaggerJsonSchema(JsonElement schema)
            : base(schema)
        {
            _schema = schema;
        }

        public string Description => SafeGetString("description");

        public string Title => SafeGetString("title");

        public string Format => SafeGetString("format");

        public string Type => SafeGetString("type");

        public IOpenApiAny Default => throw new NotImplementedException();

        public ISet<string> Required
        {
            get
            {
                HashSet<string> hs = new HashSet<string>();

                if (_schema.ValueKind != JsonValueKind.Array)
                {
                    return hs;
                }

                foreach (JsonElement je in _schema.EnumerateArray())
                {
                    hs.Add(je.GetString());
                }

                return hs;
            }
        }

        // Not supported yet
        public ISwaggerSchema AdditionalProperties => null;

        public IDictionary<string, ISwaggerSchema> Properties 
        {
            get
            {
                if (!_schema.TryGetProperty("properties", out JsonElement jprops))
                {
                    return new Dictionary<string, ISwaggerSchema>();
                }

                Dictionary<string, ISwaggerSchema> props = new Dictionary<string, ISwaggerSchema>();                                

                if (jprops.ValueKind != JsonValueKind.Object)
                {
                    return props;
                }

                foreach (JsonProperty prop in jprops.EnumerateObject())
                {
                    props.Add(prop.Name, new SwaggerJsonSchema(prop.Value));
                }

                return props;
            }
        }

        public ISwaggerSchema Items
        {
            get
            {
                if (_schema.TryGetProperty("items", out JsonElement items))
                {
                    return SwaggerJsonSchema.New(items);
                }

                if (_schema.TryGetProperty("name", out JsonElement name) || _schema.TryGetProperty("title", out name))
                {
                    throw new PowerFxConnectorException($"Missing 'items' element in array property '{name}'");
                }

                throw new PowerFxConnectorException("Missing 'items' element in array");
            }
        }

        public IList<IOpenApiAny> Enum
        {           
            get
            {
                if (_schema.TryGetProperty("enum", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
                {
                    List<IOpenApiAny> e = new List<IOpenApiAny>();

                    foreach (JsonElement je in items.EnumerateArray())
                    {
                        e.Add(new OpenApiString(je.GetString()));
                    }

                    return e;
                }

                return null;
            }

            set => throw new NotImplementedException();
        }

        // Not supported yet
        public ISwaggerReference Reference => null;

        // Not supported yet
        public ISwaggerDiscriminator Discriminator => null;

        public ISet<string> ReferenceTo => DataType == "reference" && _schema.TryGetProperty("referenceTo", out JsonElement val) && val.ValueKind == JsonValueKind.Array
                                                ? new HashSet<string>(val.EnumerateArray().Select(je => je.GetString()))
                                                : null;

        public string RelationshipName => SafeGetString("relationshipName");

        public string DataType => SafeGetString("datatype");

        private string SafeGetString(string key)
        {
            return _schema.TryGetProperty(key, out JsonElement val) ? val.GetString() : null;
        }
    }
}
