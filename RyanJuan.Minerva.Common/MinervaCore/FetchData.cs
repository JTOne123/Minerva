﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RyanJuan.Minerva.Common
{
    public sealed partial class MinervaCore
    {
        private int _firstBufferSizeForFetchModeHybrid = 4096;

        /// <summary>
        /// Execute the <see cref="DbCommand.CommandText"/> against the
        /// <see cref="DbCommand.Connection"/>, and return an enumerable of type
        /// <typeparamref name="T"/> for results using one of the <see cref="CommandBehavior"/>
        /// values.
        /// <para>
        /// If the type <typeparamref name="T"/> is class, properties will be mapping to columns
        /// by <see cref="DbColumnNameAttribute.Name"/>.
        /// If multiple properties have the same <see cref="DbColumnNameAttribute.Name"/>, then
        /// that column will be mapping into multiple properties.
        /// For those properties which does not have <see cref="DbColumnNameAttribute"/>,
        /// property's name will be used.
        /// </para>
        /// <para>
        /// This method will call
        /// <see cref="AddWithValues(DbParameterCollection, object[])"/>
        /// to add parameters into <see cref="DbCommand.Parameters"/>.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="command">Instance of <see cref="DbCommand"/>.</param>
        /// <param name="behavior">One of the <see cref="CommandBehavior"/> values.</param>
        /// <param name="fetchMode"></param>
        /// <param name="parameters">The objects which use to construct parameter.</param>
        /// <returns>Enumerable of results.</returns>
        /// <exception cref="ArgumentNullException">
        /// Parameter <paramref name="command"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <see cref="DbType"/> of parameter is not valid.
        /// </exception>
        public IEnumerable<T> FetchData<T>(
            DbCommand command,
            CommandBehavior behavior,
            FetchMode fetchMode,
            params object[] parameters)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }
            if (fetchMode == FetchMode.Default)
            {
                fetchMode = _defaultFetchMode;
            }
            AddWithValues(command.Parameters, parameters);
            var reader = command.ExecuteReader(behavior);
            if (reader.HasRows)
            {
                var type = typeof(T);
                var isObjectType = Type.GetTypeCode(type) == TypeCode.Object;
                LinkedList<PropertyInfo>[] properties = null;
                if (isObjectType)
                {
                    properties = reader.GetBindingPropertiesOfType(type);
                }
                if (fetchMode == FetchMode.Buffer)
                {
                    using (reader)
                    {
                        // FetchMode.Buffer as default
                        var list = new List<T>();
                        while (reader.Read())
                        {
                            list.Add(reader.GetValueAsT<T>(isObjectType, properties));
                        }
                        return list;
                    }
                }
                else if (fetchMode == FetchMode.Stream)
                {
                    return new FetchDataStream<T>(
                        reader,
                        isObjectType,
                        properties,
                        CancellationToken.None);
                }
                else
                {
                    var list = new List<T>();
                    while (reader.Read())
                    {
                        list.Add(reader.GetValueAsT<T>(isObjectType, properties));
                        if (list.Count == _firstBufferSizeForFetchModeHybrid)
                        {
                            return new FetchDataStream<T>(
                                reader,
                                isObjectType,
                                properties,
                                list,
                                CancellationToken.None);
                        }
                    }
                    return list;
                }
            }
            return Enumerable.Empty<T>();
        }

        /// <summary>
        /// This is the asynchronous version of
        /// <see cref="FetchData{T}(DbCommand, CommandBehavior, FetchMode, object[])"/>.
        /// Execute the <see cref="DbCommand.CommandText"/> against the
        /// <see cref="DbCommand.Connection"/>, and return an enumerable of type
        /// <typeparamref name="T"/> for results using one of the <see cref="CommandBehavior"/>
        /// values.
        /// The cancellation token can be used to request that the operation be abandoned
        /// before the command timeout elapses.
        /// <para>
        /// If the type <typeparamref name="T"/> is class, properties will be mapping to columns
        /// by <see cref="DbColumnNameAttribute.Name"/>.
        /// If multiple properties have the same <see cref="DbColumnNameAttribute.Name"/>, then
        /// that column will be mapping into multiple properties.
        /// For those properties which does not have <see cref="DbColumnNameAttribute"/>,
        /// property's name will be used.
        /// </para>
        /// <para>
        /// This method will call
        /// <see cref="AddWithValues(DbParameterCollection, object[])"/>
        /// to add parameters into <see cref="DbCommand.Parameters"/>.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="command">Instance of <see cref="DbCommand"/>.</param>
        /// <param name="behavior">One of the <see cref="CommandBehavior"/> values.</param>
        /// <param name="fetchMode"></param>
        /// <param name="cancellationToken">
        /// The token to monitor for cancellation requests.
        /// </param>
        /// <param name="parameters">The objects which use to construct parameter.</param>
        /// <returns>Enumerable of results.</returns>
        /// <exception cref="ArgumentNullException">
        /// Parameter <paramref name="command"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <see cref="DbType"/> of parameter is not valid.
        /// </exception>
        public async Task<IEnumerable<T>> FetchDataAsync<T>(
            DbCommand command,
            CommandBehavior behavior,
            FetchMode fetchMode,
            CancellationToken cancellationToken,
            params object[] parameters)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }
            AddWithValues(command.Parameters, parameters);
            var reader = await command.ExecuteReaderAsync(behavior, cancellationToken);
            if (reader.HasRows)
            {
                var type = typeof(T);
                var isObjectType = Type.GetTypeCode(type) == TypeCode.Object;
                LinkedList<PropertyInfo>[] properties = null;
                if (isObjectType)
                {
                    properties = reader.GetBindingPropertiesOfType(type);
                }
                if (fetchMode == FetchMode.Buffer)
                {
                    using (reader)
                    {
                        // FetchMode.Buffer as default
                        var list = new List<T>();
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                throw Error.OperationCanceled(cancellationToken);
                            }
                            list.Add(reader.GetValueAsT<T>(isObjectType, properties));
                        }
                        return list;
                    }
                }
                else if (fetchMode == FetchMode.Stream)
                {
                    return new FetchDataStream<T>(
                        reader,
                        isObjectType,
                        properties,
                        cancellationToken);
                }
                else
                {
                    var list = new List<T>();
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw Error.OperationCanceled(cancellationToken);
                        }
                        list.Add(reader.GetValueAsT<T>(isObjectType, properties));
                        if (list.Count == _firstBufferSizeForFetchModeHybrid)
                        {
                            return new FetchDataStream<T>(
                                reader,
                                isObjectType,
                                properties,
                                list,
                                cancellationToken);
                        }
                    }
                    return list;
                }
            }
            return Enumerable.Empty<T>();
        }

        internal sealed class FetchDataStream<T> : IEnumerable<T>, IEnumerable, IDisposable
        {
            /// <summary>
            /// For <see cref="FetchMode.Stream"/>.
            /// </summary>
            /// <param name="reader"></param>
            /// <param name="isObjectType"></param>
            /// <param name="properties"></param>
            /// <param name="cancellationToken"></param>
            public FetchDataStream(
                DbDataReader reader,
                bool isObjectType,
                LinkedList<PropertyInfo>[] properties,
                CancellationToken cancellationToken)
                : this(reader, isObjectType, properties, new List<T>(), cancellationToken) { }

            /// <summary>
            /// For <see cref="FetchMode.Hybrid"/>.
            /// </summary>
            /// <param name="reader"></param>
            /// <param name="isObjectType"></param>
            /// <param name="properties"></param>
            /// <param name="buffer"></param>
            /// <param name="cancellationToken"></param>
            public FetchDataStream(
                DbDataReader reader,
                bool isObjectType,
                LinkedList<PropertyInfo>[] properties,
                List<T> buffer,
                CancellationToken cancellationToken)
            {
                _reader = reader;
                _isObjectType = isObjectType;
                _properties = properties;
                _buffer = buffer;
                _cancellationToken = cancellationToken;
            }

            ~FetchDataStream()
            {
                Dispose();
            }

            private DbDataReader _reader = null;

            private bool _isObjectType = false;

            private LinkedList<PropertyInfo>[] _properties = null;

            private List<T> _buffer = null;

            private CancellationToken _cancellationToken;

            public void Dispose()
            {
                if (_reader != null)
                {
                    if (!_reader.IsClosed)
                    {
                        _reader.Close();
                    }
                    _reader.Dispose();
                    _reader = null;
                }
                _isObjectType = false;
                _properties = null;
                _buffer = null;
                _cancellationToken = CancellationToken.None;
                GC.SuppressFinalize(this);
            }

            public IEnumerator<T> GetEnumerator()
                => new Enumerator(this);

            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();

            private bool MoveNext(int index)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    throw Error.OperationCanceled(_cancellationToken);
                }
                lock (_buffer)
                {
                    if (index == _buffer.Count)
                    {
                        if (_reader.Read())
                        {
                            _buffer.Add(_reader.GetValueAsT<T>(_isObjectType, _properties));
                            return true;
                        }
                    }
                    else if (index < _buffer.Count)
                    {
                        return true;
                    }
                }
                return false;
            }

            internal struct Enumerator : IEnumerator<T>, IEnumerator, IDisposable
            {
                public Enumerator(FetchDataStream<T> stream)
                {
                    _stream = stream;
                    _current = default;
                    _index = 0;
                    _isDisposed = false;
                }

                private FetchDataStream<T> _stream;

                private T _current;

                private int _index;

                private bool _isDisposed;

                public T Current
                {
                    get
                    {
                        if (_isDisposed)
                        {
                            throw Error.ObjectDisposed(nameof(Enumerator));
                        }
                        return _current;
                    }
                }

                object IEnumerator.Current => Current;

                public void Dispose()
                {
                    if (_isDisposed)
                    {
                        return;
                    }
                    _stream = null;
                    _current = default;
                    _index = default;
                    _isDisposed = true;
                    GC.SuppressFinalize(this);
                }

                public bool MoveNext()
                {
                    if (_isDisposed)
                    {
                        throw Error.ObjectDisposed(nameof(Enumerator));
                    }
                    if (_index < _stream._buffer.Count ||
                        _stream.MoveNext(_index))
                    {
                        _current = _stream._buffer[_index];
                        _index++;
                        return true;
                    }
                    return false;
                }

                public void Reset()
                {
                    if (_isDisposed)
                    {
                        throw Error.ObjectDisposed(nameof(Enumerator));
                    }
                    _index = 0;
                    _current = default;
                }
            }
        }
    }
}
