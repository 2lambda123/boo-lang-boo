﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Boo.Lang.Compiler.Ast;
using Boo.Lang.Compiler.Steps.StateMachine;
using Boo.Lang.Compiler.TypeSystem;
using Boo.Lang.Compiler.TypeSystem.Builders;
using Boo.Lang.Compiler.TypeSystem.Internal;
using Boo.Lang.Environments;

namespace Boo.Lang.Compiler.Steps.AsyncAwait
{
    using System.Collections.Generic;

    internal sealed class AsyncMethodProcessor : MethodToStateMachineTransformer
    {
        /// <summary>
        /// The field of the generated async class used to store the async method builder: an instance of
        /// <see cref="AsyncVoidMethodBuilder"/>, <see cref="AsyncTaskMethodBuilder"/>, or <see cref="AsyncTaskMethodBuilder{TResult}"/> depending on the
        /// return type of the async method.
        /// </summary>
        private Field _asyncMethodBuilderField;

        /// <summary>
        /// A collection of well-known members for the current async method builder.
        /// </summary>
        private AsyncMethodBuilderMemberCollection _asyncMethodBuilderMemberCollection;

        /// <summary>
        /// The exprReturnLabel is used to label the return handling code at the end of the async state-machine
        /// method. Return expressions are rewritten as unconditional branches to exprReturnLabel.
        /// </summary>
        private InternalLabel _exprReturnLabel;

        /// <summary>
        /// The label containing a return from the method when the async method has not completed.
        /// </summary>
        private InternalLabel _exitLabel;

        /// <summary>
        /// The field of the generated async class used in generic task returning async methods to store the value
        /// of rewritten return expressions. The return-handling code then uses <c>SetResult</c> on the async method builder
        /// to make the result available to the caller.
        /// </summary>
        private InternalLocal _exprRetValue;

        /// <summary>
        /// Cached "state" of the state machine within the MoveNext method.  We work with a copy of
        /// the state to avoid shared mutable state between threads.  (Two threads can be executing
        /// in a Task's MoveNext method because an awaited task may complete after the awaiter has
        /// tested whether the subtask is complete but before the awaiter has returned)
        /// </summary>
        private InternalLocal _cachedState; 
        
        private readonly Dictionary<IType, Field> _awaiterFields;
        private int _nextAwaiterId;

        private bool _isGenericTask;

        internal AsyncMethodProcessor(
            CompilerContext context,
            InternalMethod method)
            : base(context, method)
        {
            _awaiterFields = new Dictionary<IType, Field>();
            _nextAwaiterId = 0;
        }

        public override void Run()
        {
            base.Run();
            FixAsyncMethodBody(_stateMachineConstructorInvocation);
        }

        private void FixAsyncMethodBody(MethodInvocationExpression stateMachineConstructorInvocation)
        {
            var method = _method.Method;
            // If the async method's result type is a type parameter of the method, then the AsyncTaskMethodBuilder<T>
            // needs to use the method's type parameters inside the rewritten method body. All other methods generated
            // during async rewriting are members of the synthesized state machine struct, and use the type parameters
            // structs type parameters.
            AsyncMethodBuilderMemberCollection methodScopeAsyncMethodBuilderMemberCollection;
            if (!AsyncMethodBuilderMemberCollection.TryCreate(
                    TypeSystemServices,
                    method,
                    _methodToStateMachineMapper,
                    out methodScopeAsyncMethodBuilderMemberCollection))
            {
                throw new NotImplementedException("CUstom async patterns are not supported");
            }

            var bodyBuilder = new Block();
            var builderVariable = CodeBuilder.DeclareTempLocal(method, methodScopeAsyncMethodBuilderMemberCollection.BuilderType);

            var stateMachineVariable = CodeBuilder.DeclareLocal(
                method,
                UniqueName("async"),
                _stateMachineClass.Entity);

            bodyBuilder.Add(CodeBuilder.CreateAssignment(
                CodeBuilder.CreateLocalReference(stateMachineVariable),
                stateMachineConstructorInvocation));

            // local.$builder = System.Runtime.CompilerServices.AsyncTaskMethodBuilder<typeArgs>.Create();
            bodyBuilder.Add(
                CodeBuilder.CreateAssignment(
                    CodeBuilder.CreateMemberReference(
                        CodeBuilder.CreateLocalReference(stateMachineVariable),
                        (IField)_asyncMethodBuilderField.Entity),
                    CodeBuilder.CreateMethodInvocation(methodScopeAsyncMethodBuilderMemberCollection.CreateBuilder)));

            // local.$stateField = NotStartedStateMachine
            bodyBuilder.Add(
                CodeBuilder.CreateAssignment(
                    CodeBuilder.CreateMemberReference(
                        CodeBuilder.CreateLocalReference(stateMachineVariable),
                        _state),
                    CodeBuilder.CreateIntegerLiteral(StateMachineStates.NotStartedStateMachine)));

            bodyBuilder.Add(
                CodeBuilder.CreateAssignment(
                    CodeBuilder.CreateLocalReference(builderVariable),
                    CodeBuilder.CreateMemberReference(
                        CodeBuilder.CreateLocalReference(stateMachineVariable),
                        (IField)_asyncMethodBuilderField.Entity)));

            // local.$builder.Start(ref local) -- binding to the method AsyncTaskMethodBuilder<typeArgs>.Start()
            bodyBuilder.Add(
                CodeBuilder.CreateMethodInvocation(
                    CodeBuilder.CreateLocalReference(builderVariable),
                    methodScopeAsyncMethodBuilderMemberCollection.Start.GenericInfo.ConstructMethod(_stateMachineClass.Entity),
                    CodeBuilder.CreateLocalReference(stateMachineVariable)));

            bodyBuilder.Add(method.ReturnType.Entity == TypeSystemServices.VoidType
                ? new ReturnStatement()
                : new ReturnStatement(
                    CodeBuilder.CreateMethodInvocation(
                        CodeBuilder.CreateMemberReference(
                            CodeBuilder.CreateLocalReference(stateMachineVariable),
                            (IField)_asyncMethodBuilderField.Entity),
                        methodScopeAsyncMethodBuilderMemberCollection.Task.GetGetMethod())));

            _method.Method.Body = bodyBuilder;
        }


        private Field GetAwaiterField(IType awaiterType)
        {
            Field result;

            // Awaiters of the same type always share the same slot, regardless of what await expressions they belong to.
            // Even in case of nested await expressions only one awaiter is active.
            // So we don't need to tie the awaiter variable to a particular await expression and only use its type 
            // to find the previous awaiter field.
            if (!_awaiterFields.TryGetValue(awaiterType, out result))
            {
                int slotIndex = _nextAwaiterId++;
                
                string fieldName = Context.GetUniqueName("_awaiter", slotIndex.ToString());

                result = _stateMachineClass.AddField(fieldName, awaiterType);
                _awaiterFields.Add(awaiterType, result);
            }

            return result;
        }

        /// <summary>
        /// Generate the body for <c>MoveNext()</c>.
        /// </summary>
        protected override void CreateMoveNext()
        {
            Method asyncMethod = _method.Method;

            BooMethodBuilder methodBuilder = _stateMachineClass.AddVirtualMethod("MoveNext", TypeSystemServices.VoidType);
            methodBuilder.Method.LexicalInfo = asyncMethod.LexicalInfo;
            _moveNext = methodBuilder.Entity;

            TransformLocalsIntoFields(asyncMethod);
            TransformParametersIntoFieldsInitializedByConstructor(_method.Method);

            _exprReturnLabel = CodeBuilder.CreateLabel(methodBuilder.Method, "exprReturn", 0);
            _exitLabel = CodeBuilder.CreateLabel(methodBuilder.Method, "exitLabel", 0);
            _isGenericTask = _method.ReturnType.GenericInfo != null;
            _exprRetValue = _isGenericTask
                ? CodeBuilder.DeclareTempLocal(_method.Method, _asyncMethodBuilderMemberCollection.ResultType)
                : null;

            _cachedState = CodeBuilder.DeclareLocal(methodBuilder.Method, UniqueName("state"),
                TypeSystemServices.IntType);

            var rewrittenBody = (Block)Visit(_method.Method.Body);

            var bodyBuilder = methodBuilder.Body;

            bodyBuilder.Add(CodeBuilder.CreateAssignment(
                CodeBuilder.CreateLocalReference(_cachedState),
                CodeBuilder.CreateMemberReference(_state)));

            InternalLocal exceptionLocal;
            bodyBuilder.Add(CodeBuilder.CreateTryExcept(
                this.LexicalInfo,
                new Block(
                    CodeBuilder.CreateSwitch(
                        this.LexicalInfo,
                        CodeBuilder.CreateLocalReference(_cachedState),
                        _labels),
                    rewrittenBody),
                new ExceptionHandler
                {
                    Declaration = CodeBuilder.CreateDeclaration(
                        methodBuilder.Method,
                        UniqueName("exception"),
                        TypeSystemServices.ExceptionType,
                        out exceptionLocal),
                    Block = new Block(
                        CodeBuilder.CreateFieldAssignment(
                            this.LexicalInfo, 
                            _state, 
                            CodeBuilder.CreateIntegerLiteral(StateMachineStates.FinishedStateMachine)),
                        new ExpressionStatement(
                            CodeBuilder.CreateMethodInvocation(
                                CodeBuilder.CreateMemberReference(
                                    CodeBuilder.CreateSelfReference(_stateMachineClass.Entity),
                                    (IField)_asyncMethodBuilderField.Entity),
                                _asyncMethodBuilderMemberCollection.SetException,
                                CodeBuilder.CreateLocalReference(exceptionLocal))),
                        GenerateReturn())
                }));

            // ReturnLabel (for the rewritten return expressions in the user's method body)
            bodyBuilder.Add(_exprReturnLabel.LabelStatement);

            // this.state = finishedState
            bodyBuilder.Add(CodeBuilder.CreateFieldAssignment(
                this.LexicalInfo,
                _state,
                CodeBuilder.CreateIntegerLiteral(StateMachineStates.FinishedStateMachine)));

            // builder.SetResult([RetVal])
            var setResultInvocation = CodeBuilder.CreateMethodInvocation(
                CodeBuilder.CreateMemberReference(
                    CodeBuilder.CreateSelfReference(_stateMachineClass.Entity),
                    (IField)_asyncMethodBuilderField.Entity),
                _asyncMethodBuilderMemberCollection.SetResult);
            if (_isGenericTask)
                setResultInvocation.Arguments.Add(CodeBuilder.CreateLocalReference(_exprRetValue));

            bodyBuilder.Add(new ExpressionStatement(setResultInvocation));

            // this code is hidden behind a hidden sequence point.
            bodyBuilder.Add(_exitLabel.LabelStatement);
            bodyBuilder.Add(new ReturnStatement());
        }

        private Statement GenerateReturn()
        {
            return CodeBuilder.CreateGoto(_exitLabel, 1);
        }

        #region Visitors

        public override void OnExpressionStatement(ExpressionStatement node)
        {
            if (node.Expression.NodeType == NodeType.AwaitExpression)
            {
                ReplaceCurrentNode(VisitAwaitExpression((AwaitExpression)node.Expression, null));
                return;
            }

            if (node.Expression.NodeType == NodeType.BinaryExpression 
                && ((BinaryExpression)node.Expression).Operator == BinaryOperatorType.Assign)
            {
                var expression = (BinaryExpression)node.Expression;
                if (expression.Right.NodeType == NodeType.AwaitExpression)
                {
                    ReplaceCurrentNode(VisitAwaitExpression((AwaitExpression)expression.Right, expression.Left));
                    return;
                }
            }
            base.OnExpressionStatement(node);
        }

        public override void OnAwaitExpression(AwaitExpression node)
        {
            // await expressions must, by now, have been moved to the top level.
            throw new ArgumentException("Should be unreachable");
        }

        private Block VisitAwaitExpression(AwaitExpression node, Expression resultPlace)
        {
            var expression = Visit(node.BaseExpression);
            resultPlace = Visit(resultPlace);
            var getAwaiter = node.ExpressionType.GetMembers().OfType<IMethod>().Single(m => m.Name.Equals("GetAwaiter"));
            var getResult = getAwaiter.ReturnType.GetMembers().OfType<IMethod>().Single(m => m.Name.Equals("GetResult"));
            var isCompletedMethod = getAwaiter.ReturnType.GetMembers().OfType<IProperty>().Single(p => p.Name.Equals("IsCompleted")).GetGetMethod();
            var type = node.ExpressionType;

            // The awaiter temp facilitates EnC method remapping and thus have to be long-lived.
            // It transfers the awaiter objects from the old version of the MoveNext method to the new one.
            var awaiterType = getAwaiter.ReturnType;
            var awaiterTemp = CodeBuilder.DeclareTempLocal(_moveNext.Method, awaiterType);

            var awaitIfIncomplete = new Block(
                    // temp $awaiterTemp = <expr>.GetAwaiter();
                    new ExpressionStatement(
                        CodeBuilder.CreateAssignment(
                            CodeBuilder.CreateLocalReference(awaiterTemp),
                            CodeBuilder.CreateMethodInvocation(expression, getAwaiter))),

                    // if(!($awaiterTemp.IsCompleted)) { ... }
                    new IfStatement(
                        new UnaryExpression(
                            UnaryOperatorType.LogicalNot,
                            GenerateGetIsCompleted(awaiterTemp, isCompletedMethod)),
                        GenerateAwaitForIncompleteTask(awaiterTemp),
                        null));

            var getResultCall = CodeBuilder.CreateMethodInvocation(
                CodeBuilder.CreateLocalReference(awaiterTemp),
                getResult);

            var nullAwaiter = CodeBuilder.CreateAssignment(
                CodeBuilder.CreateLocalReference(awaiterTemp),
                CodeBuilder.CreateDefaultInvocation(this.LexicalInfo, awaiterTemp.Type));
            if (resultPlace != null && type != TypeSystemServices.VoidType)
            {
                // $resultTemp = $awaiterTemp.GetResult();
                // $awaiterTemp = null;
                // $resultTemp
                InternalLocal resultTemp = CodeBuilder.DeclareTempLocal(_moveNext.Method, type);
                return new Block(
                    awaitIfIncomplete,
                    new ExpressionStatement(
                        CodeBuilder.CreateAssignment(CodeBuilder.CreateLocalReference(resultTemp), getResultCall)),
                    new ExpressionStatement(nullAwaiter),
                    new ExpressionStatement(
                        CodeBuilder.CreateAssignment(resultPlace, CodeBuilder.CreateLocalReference(resultTemp))));
            }

            // $awaiterTemp.GetResult();
            // $awaiterTemp = null;
            return new Block(
                awaitIfIncomplete,
                new ExpressionStatement(getResultCall),
                new ExpressionStatement(nullAwaiter));
        }

        private Expression GenerateGetIsCompleted(InternalLocal awaiterTemp, IMethod getIsCompletedMethod)
        {
            return CodeBuilder.CreateMethodInvocation(CodeBuilder.CreateLocalReference(awaiterTemp), getIsCompletedMethod);
        }

        private Block GenerateAwaitForIncompleteTask(InternalLocal awaiterTemp)
        {
            var stateNumber = _labels.Count;
            var resumeLabel = CreateLabel(awaiterTemp.Node);
            
            IType awaiterFieldType = awaiterTemp.Type.IsVerifierReference()
                ? TypeSystemServices.ObjectType
                : awaiterTemp.Type;

            Field awaiterField = GetAwaiterField(awaiterFieldType);

            var blockBuilder = new Block();

            // this.state = _cachedState = stateForLabel
            blockBuilder.Add(new ExpressionStatement(SetStateTo(stateNumber)));

            blockBuilder.Add(
                    // this.<>t__awaiter = $awaiterTemp
                    CodeBuilder.CreateFieldAssignment(
                    awaiterField,
                    awaiterField.Type == awaiterTemp.Type
                        ? CodeBuilder.CreateLocalReference(awaiterTemp)
                        : CodeBuilder.CreateCast(awaiterFieldType, CodeBuilder.CreateLocalReference(awaiterTemp))));

            blockBuilder.Add(GenerateAwaitOnCompleted(awaiterTemp.Type, awaiterTemp));

            blockBuilder.Add(GenerateReturn());

            blockBuilder.Add(resumeLabel);

            var awaiterFieldRef = CodeBuilder.CreateMemberReference(
                CodeBuilder.CreateSelfReference(_stateMachineClass.Entity),
                (IField)awaiterField.Entity);
            blockBuilder.Add(
                // $awaiterTemp = this.<>t__awaiter   or   $awaiterTemp = (AwaiterType)this.<>t__awaiter
                // $this.<>t__awaiter = null;
                CodeBuilder.CreateAssignment(
                    CodeBuilder.CreateLocalReference(awaiterTemp),
                    awaiterTemp.Type == awaiterField.Type
                        ? awaiterFieldRef
                        : CodeBuilder.CreateCast(awaiterTemp.Type, awaiterFieldRef)));

            blockBuilder.Add(
                CodeBuilder.CreateFieldAssignment(
                    awaiterField,
                    CodeBuilder.CreateDefaultInvocation(LexicalInfo.Empty, ((ITypedEntity)awaiterField.Entity).Type)));

            // this.state = _cachedState = NotStartedStateMachine
            blockBuilder.Add(new ExpressionStatement(SetStateTo(StateMachineStates.NotStartedStateMachine)));

            return blockBuilder;
        }

        private readonly IType ICriticalNotifyCompletionType =
            My<TypeSystemServices>.Instance.Map(typeof(ICriticalNotifyCompletion));

        private Statement GenerateAwaitOnCompleted(IType loweredAwaiterType, InternalLocal awaiterTemp)
        {
            // this.builder.AwaitOnCompleted<TAwaiter,TSM>(ref $awaiterTemp, ref this)
            //    or
            // this.builder.AwaitOnCompleted<TAwaiter,TSM>(ref $awaiterArrayTemp[0], ref this)

            InternalLocal selfTemp = _stateMachineClass.Entity.IsValueType ? null : CodeBuilder.DeclareTempLocal(_moveNext.Method, _stateMachineClass.Entity);

            var useUnsafeOnCompleted = loweredAwaiterType.IsAssignableFrom(ICriticalNotifyCompletionType);

            var onCompleted = (useUnsafeOnCompleted ?
                    _asyncMethodBuilderMemberCollection.AwaitUnsafeOnCompleted :
                    _asyncMethodBuilderMemberCollection.AwaitOnCompleted)
                .GenericInfo.ConstructMethod(loweredAwaiterType, _stateMachineClass.Entity);

            var result =
                CodeBuilder.CreateMethodInvocation(
                    CodeBuilder.CreateMemberReference(
                        CodeBuilder.CreateSelfReference(_stateMachineClass.Entity),
                        (IField)_asyncMethodBuilderField.Entity),
                    onCompleted,
                    CodeBuilder.CreateLocalReference(awaiterTemp),
                    selfTemp != null ? 
                        CodeBuilder.CreateLocalReference(selfTemp) : 
                        (Expression) CodeBuilder.CreateSelfReference(_stateMachineClass.Entity));

            if (selfTemp != null)
            {
                result = CodeBuilder.CreateEvalInvocation(
                    LexicalInfo.Empty,
                    CodeBuilder.CreateAssignment(
                        CodeBuilder.CreateLocalReference(selfTemp),
                        CodeBuilder.CreateSelfReference(_stateMachineClass.Entity)),
                    result);
            }

            return new ExpressionStatement(result);
        }

        public override void OnReturnStatement(ReturnStatement node)
        {
            Statement result = CodeBuilder.CreateGoto(_exprReturnLabel, _tryStatementStack.Count);
            if (node.Expression != null)
            {
                Debug.Assert(_isGenericTask);
                result = new Block(
                    new ExpressionStatement(
                        CodeBuilder.CreateAssignment(
                            CodeBuilder.CreateLocalReference(_exprRetValue), 
                            Visit(node.Expression))),
                    result);
            }
            ReplaceCurrentNode(result);
        }

        #endregion Visitors

        #region AbstractMemberImplementation

        protected override void PropagateReferences()
        {
            var ctor = _stateMachineConstructor;
            // propagate the external self reference if necessary
            if (_externalSelfField != null)
            {
                var type = (IType)_externalSelfField.Type.Entity;
                _stateMachineConstructorInvocation.Arguments.Add(
                    CodeBuilder.CreateSelfReference(_methodToStateMachineMapper.MapType(type)));
            }
            // propagate the necessary parameters from the original method to the state machine
            foreach (var parameter in _method.Method.Parameters)
            {
                var myParam = MapParamType(parameter);

                var entity = (InternalParameter)myParam.Entity;
                if (entity.IsUsed)
                {
                    _stateMachineConstructorInvocation.Arguments.Add(CodeBuilder.CreateReference(myParam));
                }
            }
        }

        protected override BinaryExpression SetStateTo(int num)
        {
            // this.state = _cachedState = NotStartedStateMachine
            return (BinaryExpression) CodeBuilder.CreateFieldAssignmentExpression(
                _state,
                CodeBuilder.CreateAssignment(
                    CodeBuilder.CreateLocalReference(_cachedState),
                    CodeBuilder.CreateIntegerLiteral(num)));
        }

        protected override string StateMachineClassName
        {
            get { return "$Async"; }
        }

        protected override void SaveStateMachineClass(ClassDefinition cd)
        {
            _method.Method.DeclaringType.Members.Add(cd);
        }

        protected override void SetupStateMachine()
        {
            _stateMachineClass.AddBaseType(TypeSystemServices.ValueTypeType);
            _stateMachineClass.AddBaseType(TypeSystemServices.IAsyncStateMachineType);
            _stateMachineClass.Modifiers |= TypeMemberModifiers.Final;
            var ctr = TypeSystemServices.Map(typeof(AsyncStateMachineAttribute)).GetConstructors().Single();
            _method.Method.Attributes.Add(
                CodeBuilder.CreateAttribute(
                    ctr,
                    CodeBuilder.CreateTypeofExpression(_stateMachineClass.Entity)));
            AsyncMethodBuilderMemberCollection.TryCreate(
                TypeSystemServices,
                _method.Method,
                _methodToStateMachineMapper,
                out _asyncMethodBuilderMemberCollection);

            _state = (IField)_stateMachineClass.AddInternalField(UniqueName("State"), TypeSystemServices.IntType).Entity;
            _asyncMethodBuilderField = _stateMachineClass.AddInternalField(
                UniqueName("Builder"),
                _asyncMethodBuilderMemberCollection.BuilderType);
            CreateSetStateMachine();
            PreprocessMethod();
        }

        private void PreprocessMethod()
        {
            if (ContextAnnotations.AwaitInExceptionHandler(_method.Method))
            {
                AsyncExceptionHandlerRewriter.Rewrite(_method.Method);
            }
            AwaitExpressionSpiller.Rewrite(_method.Method);
        }

        private void CreateSetStateMachine()
        {
            var method = _stateMachineClass.AddMethod("SetStateMachine", TypeSystemServices.VoidType);
            var stateMachineIntfType = TypeSystemServices.IAsyncStateMachineType;
            var input = method.AddParameter("stateMachine", stateMachineIntfType, false);
            method.Modifiers |= TypeMemberModifiers.Virtual | TypeMemberModifiers.Final;
            method.Method.ExplicitInfo = new ExplicitMemberInfo
            {
                InterfaceType = (SimpleTypeReference)CodeBuilder.CreateTypeReference(stateMachineIntfType),
                Entity = TypeSystemServices.IAsyncStateMachineType.GetMembers().OfType<IMethod>()
                    .Single(m => m.Name.Equals("SetStateMachine"))
            };
            method.Body.Add(
                CodeBuilder.CreateMethodInvocation(
                    CodeBuilder.CreateMemberReference(
                        CodeBuilder.CreateSelfReference(_stateMachineClass.Entity),
                        (IField) _asyncMethodBuilderField.Entity),
                    _asyncMethodBuilderMemberCollection.SetStateMachine,
                    CodeBuilder.CreateReference(input)));
        }

        protected override BooMethodBuilder CreateConstructor(BooClassBuilder builder)
        {
            BooMethodBuilder constructor = builder.AddConstructor();
            return constructor;
        }

        #endregion

    }
}

