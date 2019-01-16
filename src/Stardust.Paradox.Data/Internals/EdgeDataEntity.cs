﻿using Newtonsoft.Json;
using Stardust.Paradox.Data.Annotations;
using Stardust.Paradox.Data.CodeGeneration;
using Stardust.Particles;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Stardust.Paradox.Data.Internals
{

	public abstract class EdgeDataEntity<TIn, TOut> : IGraphEntityInternal, IEdgeEntityInternal, IEdge<TIn, TOut> where TIn : IVertex where TOut : IVertex
	{

		private string gremlinUpdateStatement = "";
		private readonly ConcurrentDictionary<string, IInlineCollection> _inlineCollections = new ConcurrentDictionary<string, IInlineCollection>();
		public string EntityKey
		{
			get => _entityKey;
			set => _entityKey = value;
		}

		public bool EagerLoading
		{
			get => false;
			set { }
		}

		public string GetUpdateStatement()
		{
			return _edgeSelector + gremlinUpdateStatement;
		}
		public void SetContext(GraphContextBase graphContextBase)
		{
			_context = graphContextBase;
		}

		Task IGraphEntityInternal.Eager(bool doEagerLoad)
		{
			return Task.CompletedTask;
		}

		public void DoLoad(dynamic o)
		{
			if (Label != o.label.ToString()) throw new InvalidCastException($"Unable to cast graph item with label {o.label} to {Label}");
			InVertexId = o.inV.ToString();
			OutVertextId = o.outV.ToString();

		}

		internal GraphContextBase _context;
		private bool _isLoading;
		protected internal string _entityKey;
		private string _edgeSelector;
		private TIn _inV;
		private TOut _outV;

		public abstract string Label { get; }
		public event PropertyChangedHandler PropertyChanged;
		public event PropertyChangingHandler PropertyChanging;
		public async Task<TIn> InVAsync()
		{
			return _inV != null ? _inV : (_inV = await _context.GetOrCreate<TIn>(InVertexId));
		}

		void IEdgeEntityInternal.SetInVertex(IVertex vertex)
		{
			var old = _inV as GraphDataEntity;
			var newV = vertex as GraphDataEntity;
			if (old?._entityKey == newV?._entityKey) return;
			_inV = (TIn)vertex;
			MakeUpdateStatement();
		}

		private string MakeUpdateStatement()
		{
			if (_outV != null && _inV != null)
			{
				_edgeSelector = "";
				_edgeSelector +=
					$"g.V('{((IGraphEntityInternal)_outV).EntityKey}').as('a').V('{((IGraphEntityInternal)_outV).EntityKey}').addE('{Label}').as('b').from('a').to('b').property('id','{_entityKey}')";
				IsDirty = true;

			}
			return _edgeSelector;
		}

		void IEdgeEntityInternal.SetOutVertex(IVertex vertex)
		{
			var old = _inV as GraphDataEntity;
			var newV = vertex as GraphDataEntity;
			if (old?._entityKey == newV?._entityKey) return;
			_outV = (TOut)vertex;
			MakeUpdateStatement();
			IsDirty = true;
		}

		public string InVertexId { get; internal set; }

		public string OutVertextId { get; internal set; }

		public async Task<TOut> OutVAsync()
		{
			return _outV != null ? _outV : (_outV = await _context.GetOrCreate<TOut>(OutVertextId));
		}

		public virtual void OnPropertyChanged(object value, string propertyName = null)
		{
			if (_isLoading) return;
			if (IsDeleted)
				throw new EntityDeletedException(
					$"Entitiy {GetType().GetInterfaces().First().Name}.{_entityKey} is marked as deleted.");
			if (_edgeSelector.IsNullOrWhiteSpace()) return;

			if (value != null)
			{
				PropertyChanged?.Invoke(this, new PropertyChangedHandlerArgs(value, propertyName));
				gremlinUpdateStatement += $".property('{propertyName.ToCamelCase()}',{GetValue(value)})";
				IsDirty = true;
			}
		}

		protected IInlineCollection<T> GetInlineCollection<T>(string name)
		{
			if (_inlineCollections.TryGetValue(name, out IInlineCollection i)) return (IInlineCollection<T>)i;
			i = new InlineCollection<T>(this, name);
			_inlineCollections.TryAdd(name, i);
			return (IInlineCollection<T>)i;
		}

		private string GetValue(object value)
		{
			if (value == null) return null;
			switch (value)
			{
				case string s:
					var r = $"'{EscapeString(s)}'";
					if (r == "'''") return "''";
					return r;
				case DateTime time:
					return $"{time.Ticks}";
				case int no:
					return no.ToString(CultureInfo.InvariantCulture);
				case decimal dec:
					return dec.ToString(CultureInfo.InvariantCulture);
				case long lng:
					return lng.ToString(CultureInfo.InvariantCulture);
				case float flt:
					return flt.ToString(CultureInfo.InvariantCulture);
				case double dbl:
					return dbl.ToString(CultureInfo.InvariantCulture);
				case bool yn:
					return yn.ToString(CultureInfo.InvariantCulture).ToLower();
				case IInlineCollection i:
					return $"'{i.ToTransferData()}'";
			}
			throw new ArgumentException("Unknown type", nameof(value));
		}

		private static object EscapeString(string value)
		{
			if (value.IsNullOrWhiteSpace()) return value;
			return value.Replace("\\", "")
				.Replace("'", "\\'")
				.Replace("`", "")
				.Replace("´", "");
		}
		public bool IsDeleted
		{
			get;
			protected internal set;
		}

		public bool OnPropertyChanging(object newValue, object oldValue, string propertyName = null)
		{
			if (_isLoading) return true;
			if ((IsNew && newValue != null) || newValue?.ToString() != oldValue?.ToString())
			{
				PropertyChanging?.Invoke(this, new PropertyChangingHandlerArgs(newValue, oldValue, propertyName));
				return true;
			}
			return false;
		}

		public bool IsNew { get; private set; }

		public void Reset(bool isNew)
		{
			_isLoading = false;
			IsNew = isNew;
			IsDeleted = false;
			gremlinUpdateStatement = "";

			if (isNew)
				MakeUpdateStatement();
			else
				_edgeSelector = $"g.E('{_entityKey}')";
			IsDirty = false;
			IsNew = isNew;
			IsDeleted = false;
		}

		public void Delete()
		{
			gremlinUpdateStatement = ".drop()";
			IsDeleted = true;
			IsDirty = true;
		}

		[JsonIgnore]
		public bool IsDirty { get; internal set; }

		[JsonIgnore]
		public string Type => "edge";
	}
}