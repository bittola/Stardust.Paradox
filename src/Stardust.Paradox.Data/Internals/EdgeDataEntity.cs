﻿using Newtonsoft.Json;
using Stardust.Paradox.Data.Annotations;
using Stardust.Paradox.Data.CodeGeneration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stardust.Paradox.Data.Annotations.DataTypes;

namespace Stardust.Paradox.Data.Internals
{

	public abstract class EdgeDataEntity<TIn, TOut> : IGraphEntityInternal, IEdgeEntityInternal, IEdge<TIn, TOut> where TIn : IVertex where TOut : IVertex
	{

		//private string gremlinUpdateStatement = "";
		private Dictionary<string, Update> UpdateChain = new Dictionary<string, Update>();
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

		public string GetUpdateStatement(bool parameterized)
		{
			if (_edgeSelector == null) MakeUpdateStatement(parameterized);
			if (parameterized)
				return _edgeSelector + string.Join("", UpdateChain.Select(u => u.Value.ParameterizedUpdateStatement));
			return _edgeSelector + string.Join("", UpdateChain.Select(u => u.Value.UpdateStatement));
		}
		public void SetContext(GraphContextBase graphContextBase, bool connectorCanParameterizeQueries)
		{
			_parametrized = connectorCanParameterizeQueries;
			_context = graphContextBase;
		}

		Task IGraphEntityInternal.Eager(bool doEagerLoad)
		{
			return Task.CompletedTask;
		}

		public void DoLoad(dynamic o)
		{
			_isLoading = true;
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
			InVertexId = newV?.EntityKey;
		}

		private Dictionary<string, object> selectorParameters = new Dictionary<string, object>();
		private bool _parametrized;

		private string MakeUpdateStatement(bool parameterized)
		{
			if (InVertexId != null && OutVertextId != null)
			{
				if (parameterized)
				{
					selectorParameters.Clear();
					selectorParameters.Add("inVId", InVertexId);
					selectorParameters.Add("outVID", OutVertextId);
					selectorParameters.Add("___label", Label);
					selectorParameters.Add("___ekey", _entityKey);
					_edgeSelector = "";
					_edgeSelector +=
						$"g.V(inVId).as('a').V(outVID).as('b').addE(___label).from('b').to('a').property('id',___ekey)";
					IsDirty = true;
				}
				else
				{
					_edgeSelector = "";
					_edgeSelector +=
						$"g.V('{InVertexId.EscapeGremlinString()}').as('a').V('{OutVertextId.EscapeGremlinString()}').as('b').addE('{Label}').from('b').to('a').property('id','{_entityKey}')";
					IsDirty = true;
				}

			}
			return _edgeSelector;
		}

		void IEdgeEntityInternal.SetOutVertex(IVertex vertex)
		{
			var old = _inV as GraphDataEntity;
			var newV = vertex as GraphDataEntity;
			if (old?._entityKey == newV?._entityKey) return;
			_outV = (TOut)vertex;
			OutVertextId = newV.EntityKey;
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
			if (value != null)
			{
				PropertyChanged?.Invoke(this, new PropertyChangedHandlerArgs(value, propertyName));
				if (UpdateChain.TryGetValue(propertyName.ToCamelCase(), out var update))
					update.Value = GetValue(value);
				else UpdateChain.Add(propertyName.ToCamelCase(), new Update { PropertyName = propertyName.ToCamelCase(), Value = GetValue(value) });

				IsDirty = true;
			}
		}
		internal object GetValue(object value)
		{
			if (value == null) return null;
			switch (value)
			{
				case IInlineCollection i:
					return i.ToTransferData();
				case EpochDateTime e:
					return e.Epoch;
				default:
					return value;
			}
		}
		protected IInlineCollection<T> GetInlineCollection<T>(string name)
		{
			if (_inlineCollections.TryGetValue(name, out IInlineCollection i)) return (IInlineCollection<T>)i;
			i = new InlineCollection<T>(this, name);
			_inlineCollections.TryAdd(name, i);
			return (IInlineCollection<T>)i;
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
			UpdateChain.Clear();

			if (_parametrized)
			{
				selectorParameters.Clear();
				
				if (!isNew)
				{
					selectorParameters.Add("___ekey", _entityKey);
					_edgeSelector = $"g.E(___ekey)";
				} 
			}
			else
			{
				if (!isNew)
				{
					_edgeSelector = $"g.E('{_entityKey.EscapeGremlinString()}')";
				}
			}
			IsDirty = isNew;
			IsNew = isNew;
			IsDeleted = false;
		}

		public void Delete()
		{
			UpdateChain.Add("____drop____", new Update { Parameterless = ".drop()" });
			IsDeleted = true;
			IsDirty = true;
		}

		[JsonIgnore]
		public bool IsDirty { get; internal set; }

		[JsonIgnore]
		public string _EntityType => "edge";

		public Dictionary<string, object> GetParameterizedValues()
		{
			var p = UpdateChain.Where(v => v.Value.HasParameters)
				.ToDictionary(k => $"__{k.Value.PropertyName}", v => v.Value.Value);
			foreach (var selectorParameter in selectorParameters)
			{
				p.Add(selectorParameter.Key, selectorParameter.Value);
			}
			return p;
		}
	}
}