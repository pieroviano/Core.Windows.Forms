// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System.Collections.Specialized;
using System.Web.Helpers;

namespace System.Web.Mvc
{
    // Concrete implementation for the IUnvalidatedRequestValues helper interface

    internal sealed class UnvalidatedRequestValuesWrapper : IUnvalidatedRequestValues
    {
        private readonly System.Web.Helpers.UnvalidatedRequestValues _unvalidatedValues;

        public UnvalidatedRequestValuesWrapper (System.Web.Helpers.UnvalidatedRequestValues unvalidatedValues)
        {
            _unvalidatedValues = unvalidatedValues;
        }

        public NameValueCollection Form
        {
            get { return _unvalidatedValues.Form; }
        }

        public NameValueCollection QueryString
        {
            get { return _unvalidatedValues.QueryString; }
        }

        public string this[string key]
        {
            get { return _unvalidatedValues[key]; }
        }
    }
}
