﻿using Microsoft.Extensions.Logging;
using System.Text;

namespace Jering.KeyValueStore.Tests
{
    public class StringBuilderProvider : ILoggerProvider
    {
        private readonly StringBuilder _stringBuilder;

        public StringBuilderProvider(StringBuilder stringBuilder)
        {
            _stringBuilder = stringBuilder;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new StringBuilderLogger(_stringBuilder);
        }

        public void Dispose()
        {
        }
    }
}
