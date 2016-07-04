#region license
// Copyright (c) 2009 Rodrigo B. de Oliveira (rbo@acm.org)
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//     this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright notice,
//     this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//     * Neither the name of Rodrigo B. de Oliveira nor the names of its
//     contributors may be used to endorse or promote products derived from this
//     software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
// THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
#endregion

//
// DO NOT EDIT THIS FILE!
//
// This file was generated automatically by astgen.boo.
//
namespace Boo.Lang.Compiler.Ast
{	
	using System.Collections;
	using System.Runtime.Serialization;
	
	[System.Serializable]
	public partial class BlockExpression : Expression, INodeWithParameters, INodeWithBody
	{
		protected ParameterDeclarationCollection _parameters;

		protected TypeReference _returnType;

		protected bool _isExpressionTree;

		protected Block _body;


		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		new public BlockExpression CloneNode()
		{
			return (BlockExpression)Clone();
		}
		
		/// <summary>
		/// <see cref="Node.CleanClone"/>
		/// </summary>
		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		new public BlockExpression CleanClone()
		{
			return (BlockExpression)base.CleanClone();
		}

		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		override public NodeType NodeType
		{
			get { return NodeType.BlockExpression; }
		}

		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		override public void Accept(IAstVisitor visitor)
		{
			visitor.OnBlockExpression(this);
		}

		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		override public bool Matches(Node node)
		{	
			if (node == null) return false;
			if (NodeType != node.NodeType) return false;
			var other = ( BlockExpression)node;
			if (!Node.AllMatch(_parameters, other._parameters)) return NoMatch("BlockExpression._parameters");
			if (!Node.Matches(_returnType, other._returnType)) return NoMatch("BlockExpression._returnType");
			if (_isExpressionTree != other._isExpressionTree) return NoMatch("BlockExpression._isExpressionTree");
			if (!Node.Matches(_body, other._body)) return NoMatch("BlockExpression._body");
			return true;
		}

		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		override public bool Replace(Node existing, Node newNode)
		{
			if (base.Replace(existing, newNode))
			{
				return true;
			}
			if (_parameters != null)
			{
				ParameterDeclaration item = existing as ParameterDeclaration;
				if (null != item)
				{
					ParameterDeclaration newItem = (ParameterDeclaration)newNode;
					if (_parameters.Replace(item, newItem))
					{
						return true;
					}
				}
			}
			if (_returnType == existing)
			{
				this.ReturnType = (TypeReference)newNode;
				return true;
			}
			if (_body == existing)
			{
				this.Body = (Block)newNode;
				return true;
			}
			return false;
		}

		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		override public object Clone()
		{
		
			BlockExpression clone = new BlockExpression();
			clone._lexicalInfo = _lexicalInfo;
			clone._endSourceLocation = _endSourceLocation;
			clone._documentation = _documentation;
			clone._isSynthetic = _isSynthetic;
			clone._entity = _entity;
			if (_annotations != null) clone._annotations = (Hashtable)_annotations.Clone();
			clone._expressionType = _expressionType;
			if (null != _parameters)
			{
				clone._parameters = _parameters.Clone() as ParameterDeclarationCollection;
				clone._parameters.InitializeParent(clone);
			}
			if (null != _returnType)
			{
				clone._returnType = _returnType.Clone() as TypeReference;
				clone._returnType.InitializeParent(clone);
			}
			clone._isExpressionTree = _isExpressionTree;
			if (null != _body)
			{
				clone._body = _body.Clone() as Block;
				clone._body.InitializeParent(clone);
			}
			return clone;


		}

		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		override internal void ClearTypeSystemBindings()
		{
			_annotations = null;
			_entity = null;
			_expressionType = null;
			if (null != _parameters)
			{
				_parameters.ClearTypeSystemBindings();
			}
			if (null != _returnType)
			{
				_returnType.ClearTypeSystemBindings();
			}
			if (null != _body)
			{
				_body.ClearTypeSystemBindings();
			}

		}
	

		[System.Xml.Serialization.XmlArray]
		[System.Xml.Serialization.XmlArrayItem(typeof(ParameterDeclaration))]
		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		public ParameterDeclarationCollection Parameters
		{
			

			get { return _parameters ?? (_parameters = new ParameterDeclarationCollection(this)); }
			set
			{
				if (_parameters != value)
				{
					_parameters = value;
					if (null != _parameters)
					{
						_parameters.InitializeParent(this);
					}
				}
			}

		}
		

		[System.Xml.Serialization.XmlElement]
		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		public TypeReference ReturnType
		{
			
			get { return _returnType; }
			set
			{
				if (_returnType != value)
				{
					_returnType = value;
					if (null != _returnType)
					{
						_returnType.InitializeParent(this);
					}
				}
			}

		}
		

		[System.Xml.Serialization.XmlElement]
		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		public bool IsExpressionTree
		{
			
			get { return _isExpressionTree; }
			set { _isExpressionTree = value; }

		}
		

		[System.Xml.Serialization.XmlElement]
		[System.CodeDom.Compiler.GeneratedCodeAttribute("astgen.boo", "1")]
		public Block Body
		{
			
			get
			{ 
				if (_body == null)
				{
					_body = new Block();
					_body.InitializeParent(this);
				}
				return _body;
			}
			set
			{
				if (_body != value)
				{
					_body = value;
					if (null != _body)
					{
						_body.InitializeParent(this);
					}
				}
			}

		}
		

	}
}

