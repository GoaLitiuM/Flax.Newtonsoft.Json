#region License
// Copyright (c) 2007 James Newton-King
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Utilities;

namespace Newtonsoft.Json.Serialization
{
    public abstract class JsonSerializerInternalBase
    {
        private class ReferenceEqualsEqualityComparer : IEqualityComparer<object>
        {
            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            int IEqualityComparer<object>.GetHashCode(object obj)
            {
                // put objects in a bucket based on their reference
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private ErrorContext _currentErrorContext;
        private BidirectionalDictionary<string, object> _mappings;

        internal readonly JsonSerializer Serializer;
#if HAVE_TRACE_WRITER
		internal readonly ITraceWriter TraceWriter;
#endif
		protected JsonSerializerProxy InternalSerializer;

        protected JsonSerializerInternalBase(JsonSerializer serializer)
        {
            ValidationUtils.ArgumentNotNull(serializer, nameof(serializer));

            Serializer = serializer;
#if HAVE_TRACE_WRITER
			TraceWriter = serializer.TraceWriter;
#endif
		}

        internal BidirectionalDictionary<string, object> DefaultReferenceMappings
        {
            get
            {
                // override equality comparer for object key dictionary
                // object will be modified as it deserializes and might have mutable hashcode
                if (_mappings == null)
                {
                    _mappings = new BidirectionalDictionary<string, object>(
                        EqualityComparer<string>.Default,
                        new ReferenceEqualsEqualityComparer(),
                        "A different value already has the Id '{0}'.",
                        "A different Id has already been assigned for value '{0}'. This error may be caused by an object being reused multiple times during deserialization and can be fixed with the setting ObjectCreationHandling.Replace.");
                }

                return _mappings;
            }
        }

        protected NullValueHandling ResolvedNullValueHandling(JsonObjectContract containerContract, JsonProperty property)
        {
            NullValueHandling resolvedNullValueHandling =
                property.NullValueHandling
                ?? containerContract?.ItemNullValueHandling
                ?? Serializer._nullValueHandling;

            return resolvedNullValueHandling;
        }

        private ErrorContext GetErrorContext(object currentObject, object member, string path, Exception error)
        {
            if (_currentErrorContext == null)
            {
                _currentErrorContext = new ErrorContext(currentObject, member, path, error);
            }

            if (_currentErrorContext.Error != error)
            {
                throw new InvalidOperationException("Current error context error is different to requested error.");
            }

            return _currentErrorContext;
        }

        protected void ClearErrorContext()
        {
            if (_currentErrorContext == null)
            {
                throw new InvalidOperationException("Could not clear error context. Error context is already null.");
            }

            _currentErrorContext = null;
        }

        protected bool IsErrorHandled(object currentObject, JsonContract contract, object keyValue, IJsonLineInfo lineInfo, string path, Exception ex)
        {
            ErrorContext errorContext = GetErrorContext(currentObject, keyValue, path, ex);

#if HAVE_TRACE_WRITER
			if (TraceWriter != null && TraceWriter.LevelFilter >= TraceLevel.Error && !errorContext.Traced)
            {
                // only write error once
                errorContext.Traced = true;

                // kind of a hack but meh. might clean this up later
                string message = (GetType() == typeof(JsonSerializerInternalWriter)) ? "Error serializing" : "Error deserializing";
                if (contract != null)
                {
                    message += " " + contract.UnderlyingType;
                }
                message += ". " + ex.Message;

                // add line information to non-json.net exception message
                if (!(ex is JsonException))
                {
                    message = JsonPosition.FormatMessage(lineInfo, path, message);
                }

                TraceWriter.Trace(TraceLevel.Error, message, ex);
            }
#endif
#if HAVE_RUNTIME_SERIALIZATION
            // attribute method is non-static so don't invoke if no object
            if (contract != null && currentObject != null)
            {
                contract.InvokeOnError(currentObject, Serializer.Context, errorContext);
            }
#endif

			if (!errorContext.Handled)
            {
                Serializer.OnError(new ErrorEventArgs(currentObject, errorContext));
            }

            return errorContext.Handled;
        }
    }
}