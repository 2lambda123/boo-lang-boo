﻿using System;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Boo.Lang.Compiler.Ast;
using Boo.Lang.Compiler.TypeSystem;
using Boo.Lang.Compiler.TypeSystem.Reflection;

namespace Boo.Lang.Compiler.Steps.Ecma335
{
    internal class AttributeBuilder 
    {
        private readonly Ast.Attribute _attr;

        public BlobHandle Handle { get; private set; }
        public EntityHandle ConstructorHandle { get; }

        private readonly TypeSystemBridge _ts;

        public AttributeBuilder(Ast.Attribute attr, TypeSystemBridge ts)
        {
            _attr = attr;
            ConstructorHandle = ts.LookupMethod((IMethod)_attr.Entity);
            _ts = ts;
        }

        public void Build()
        {
            var entity = (IConstructor)_attr.Entity;
            var varargs = entity.AcceptVarArgs;
            var constructorParams = entity.GetParameters();
            var fixedParamCount = constructorParams.Length;
            if (varargs) --fixedParamCount;
            var encoder = new BlobEncoder(new BlobBuilder());
            encoder.CustomAttributeSignature(out var fixedBuilder, out var namedBuilder);
            for (int i = 0; i < fixedParamCount; ++i)
            {
                EncodeValue(fixedBuilder.AddArgument(), _attr.Arguments[i], constructorParams[i].Type);
            }
            if (varargs)
            {
                var arrBuilder = fixedBuilder.AddArgument().Vector().Count(_attr.Arguments.Count - fixedParamCount);
                var varargElementType = entity.GetParameters()[fixedParamCount].Type.ElementType;
                for (int i = fixedParamCount; i < _attr.Arguments.Count; ++i)
                {
                    EncodeValue(arrBuilder.AddLiteral(), _attr.Arguments[i], varargElementType);
                }
            }
            var enc = namedBuilder.Count(_attr.NamedArguments.Count);
            foreach (var nArg in _attr.NamedArguments)
            {
                enc.AddArgument(nArg.First.Entity is IField, out var typeEncoder, out var nameEncoder, out var valueEncoder);
                var propType = ((ITypedEntity)nArg.First.Entity).Type;
                EncodeType(typeEncoder, propType);
                nameEncoder.Name(nArg.First.Entity.Name);
                EncodeValue(valueEncoder, nArg.Second, propType);
            }
            Handle = _ts.AssemblyBuilder.GetOrAddBlob(encoder.Builder);
        }

        private static void EncodeType(NamedArgumentTypeEncoder encoder, IType type)
        {
            if ((type as ExternalType)?.ActualType == typeof(Type))
            {
                encoder.ScalarType().SystemType();
            }
            else if (type.IsArray)
            {
                EncodeType(encoder.SZArray().ElementType(), type.ElementType);
            }
            else
            {
                // Work out the primitive type code
                var primTypeCode = GetPrimitiveTypeCode(type);
                encoder.ScalarType().PrimitiveType(primTypeCode);
            }
        }

        static void EncodeType(CustomAttributeElementTypeEncoder encoder, IType type)
        {
            if ((type as ExternalType)?.ActualType == typeof(Type))
            {
                encoder.SystemType();
            }
            else
            {
                // Work out the primitive type code
                var primTypeCode = GetPrimitiveTypeCode(type);
                encoder.PrimitiveType(primTypeCode);
            }
        }

        private static PrimitiveSerializationTypeCode GetPrimitiveTypeCode(IType type)
        {
            var sysType = (type as ExternalType)?.ActualType;
            if (sysType == null)
            {
                throw new EcmaBuildException($"{type} is not a primitive type");
            }

            return Type.GetTypeCode(sysType) switch
            {
                TypeCode.Boolean => PrimitiveSerializationTypeCode.Boolean,
                TypeCode.Char => PrimitiveSerializationTypeCode.Char,
                TypeCode.SByte => PrimitiveSerializationTypeCode.SByte,
                TypeCode.Byte => PrimitiveSerializationTypeCode.Byte,
                TypeCode.Int16 => PrimitiveSerializationTypeCode.Int16,
                TypeCode.UInt16 => PrimitiveSerializationTypeCode.UInt16,
                TypeCode.Int32 => PrimitiveSerializationTypeCode.Int32,
                TypeCode.UInt32 => PrimitiveSerializationTypeCode.UInt32,
                TypeCode.Int64 => PrimitiveSerializationTypeCode.Int64,
                TypeCode.UInt64 => PrimitiveSerializationTypeCode.UInt64,
                TypeCode.Single => PrimitiveSerializationTypeCode.Single,
                TypeCode.Double => PrimitiveSerializationTypeCode.Double,
                TypeCode.String => PrimitiveSerializationTypeCode.String,
                _ => throw new EcmaBuildException($"{type} is not a primitive type"),
            };
        }

        private void EncodeValue(LiteralEncoder encoder, Expression arg, IType expectedType = null)
        {
            if (expectedType == _ts.ObjectTypeEntity)
            {
                EncodeTaggedValue(encoder, arg);
            }
            else if (arg is ArrayLiteralExpression array)
            {
                var subEncoder = encoder.Vector().Count(array.Items.Count);
                foreach (var el in array.Items)
                {
                    EncodeValue(subEncoder.AddLiteral(), el);
                }
            }
            else EncodeScalarValue(encoder.Scalar(), arg, expectedType);
        }

        void EncodeScalarValue(ScalarEncoder encoder, Expression arg, IType expectedType = null)
        { 
            if (arg is TypeofExpression type)
            {
                encoder.SystemType(type.Type.Entity.FullName);
            }
            else if (arg is ReferenceExpression && arg.ExpressionType == _ts.TypeTypeEntity)
            {
                encoder.SystemType(arg.ExpressionType.FullName);
            }
            else if (arg is NullLiteralExpression)
            {
                if (arg.ExpressionType.IsArray)
                {
                    encoder.NullArray();
                }
                else
                {
                    encoder.Constant(null);
                }
            }
            else
            {
                encoder.Constant(_ts.GetExpressionValue(arg, expectedType));
            }
        }

        private void EncodeTaggedValue(LiteralEncoder encoder, Expression arg)
        {
            if (arg.ExpressionType.IsArray)
            {
                throw new EcmaBuildException("Tagged arrays are not supported");
            }
            encoder.TaggedScalar(out var typeEnc, out var valueEnc);
            var exprType = arg.ExpressionType;
            if (exprType == _ts.TypeTypeEntity)
            {
                typeEnc.SystemType();
                valueEnc.SystemType(exprType.FullName);
            }
            else if (exprType.IsEnum)
            {
                typeEnc.Enum(exprType.FullName);
                valueEnc.Constant(arg.Entity.Name);
            }
            else
            {
                typeEnc.PrimitiveType(GetPrimitiveTypeCode(arg.ExpressionType));
                EncodeScalarValue(valueEnc, arg, arg.ExpressionType);
            }
        }
    }
}
