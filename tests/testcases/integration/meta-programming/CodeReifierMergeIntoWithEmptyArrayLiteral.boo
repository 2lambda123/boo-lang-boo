"""
System.String
"""
import Boo.Lang.Environments

import Boo.Lang.Compiler
import Boo.Lang.Compiler.Ast
import Boo.Lang.Compiler.Steps
import Boo.Lang.Compiler.TypeSystem.Services

interface IFoo:
	def Bar() as string**
	
class ImplementIFoo(AbstractVisitorCompilerStep):
	
	override def Run():
		Visit(CompileUnit)
		
	override def LeaveClassDefinition(node as ClassDefinition):
		impl = [|
			class _($IFoo):
				def Bar():
					yield (,)
						
					
		|]
		my(CodeReifier).MergeInto(node, impl)
				
	
module = [|
	import System
	
	class Foo:
		pass
|]

pipeline = Pipelines.CompileToMemory()
pipeline.InsertAfter(Steps.TypeInference, ImplementIFoo())

parameters = CompilerParameters(Pipeline: pipeline)
parameters.References.Add(typeof(IFoo).Assembly)

result = BooCompiler(parameters).Run(CompileUnit(module))
assert len(result.Errors) == 0, result.Errors.ToString(true)

foo as IFoo = result.GetGeneratedAssembly().GetType("Foo")()
for item in foo.Bar():
	print item.GetType().GetElementType()
