﻿using System;
using System.Collections.Generic;
using System.Linq;
using Boo.Lang.Compiler.TypeSystem;
using Boo.Lang.Compiler.TypeSystem.Generics;

namespace Boo.Lang.Compiler.Steps.Generators
{
    public class GeneratorTypeReplacer : TypeReplacer
    {
        private readonly Stack<IType> _inConstructedTypes = new Stack<IType>();

        public override IType MapType(IType sourceType)
        {
            var result = base.MapType(sourceType);
            if (result.ConstructedInfo == null && result.GenericInfo != null && !_inConstructedTypes.Contains(sourceType))
                result = ConstructType(sourceType);
            return result;
        }

        public override IType MapConstructedType(IType sourceType)
        {
            var baseType = sourceType.ConstructedInfo.GenericDefinition;
            _inConstructedTypes.Push(baseType);
            try
            {
                return base.MapConstructedType(sourceType);
            }
            finally
            {
                _inConstructedTypes.Pop();
            }
        }

        private IType ConstructType(IType sourceType)
        {
            var parameters = sourceType.GenericInfo.GenericParameters;
            var typeMap = new List<IType>();
            foreach (var param in parameters)
            {
                var match = TypeMap.Keys.FirstOrDefault(t => t.Name.Equals(param.Name));
                if (match == null)
                    break;
                typeMap.Add(match);
            }
            if (typeMap.Count > 0)
                return sourceType.GenericInfo.ConstructType(typeMap.ToArray());
            return sourceType;
        }
    }
}
