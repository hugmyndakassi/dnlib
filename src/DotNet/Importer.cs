﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using dot10.DotNet.MD;

namespace dot10.DotNet {
	/// <summary>
	/// Imports <see cref="Type"/>s, <see cref="ConstructorInfo"/>s, <see cref="MethodInfo"/>s
	/// and <see cref="FieldInfo"/>s as references
	/// </summary>
	public struct Importer {
		ModuleDef ownerModule;
		RecursionCounter recursionCounter;
		bool fixSignature;

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="ownerModule">The module that will own all references</param>
		public Importer(ModuleDef ownerModule) {
			this.ownerModule = ownerModule;
			this.recursionCounter = new RecursionCounter();
			this.fixSignature = false;
		}

		/// <summary>
		/// Imports a <see cref="Type"/> as a <see cref="ITypeDefOrRef"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <returns>The imported type or <c>null</c> if <paramref name="type"/> is invalid</returns>
		public ITypeDefOrRef Import(Type type) {
			return ownerModule.UpdateRowId(ImportAsTypeSig(type).ToTypeDefOrRef());
		}

		/// <summary>
		/// Imports a <see cref="Type"/> as a <see cref="ITypeDefOrRef"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <param name="requiredModifiers">A list of all required modifiers or <c>null</c></param>
		/// <param name="optionalModifiers">A list of all optional modifiers or <c>null</c></param>
		/// <returns>The imported type or <c>null</c> if <paramref name="type"/> is invalid</returns>
		public ITypeDefOrRef Import(Type type, IList<Type> requiredModifiers, IList<Type> optionalModifiers) {
			return ownerModule.UpdateRowId(ImportAsTypeSig(type, requiredModifiers, optionalModifiers).ToTypeDefOrRef());
		}

		/// <summary>
		/// Imports a <see cref="Type"/> as a <see cref="TypeSig"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <returns>The imported type or <c>null</c> if <paramref name="type"/> is invalid</returns>
		public TypeSig ImportAsTypeSig(Type type) {
			return ImportAsTypeSig(type, false);
		}

		TypeSig ImportAsTypeSig(Type type, bool treatAsGenericInst) {
			if (type == null)
				return null;
			switch (treatAsGenericInst ? ElementType.GenericInst : type.GetElementType2()) {
			case ElementType.Void:		return ownerModule.CorLibTypes.Void;
			case ElementType.Boolean:	return ownerModule.CorLibTypes.Boolean;
			case ElementType.Char:		return ownerModule.CorLibTypes.Char;
			case ElementType.I1:		return ownerModule.CorLibTypes.SByte;
			case ElementType.U1:		return ownerModule.CorLibTypes.Byte;
			case ElementType.I2:		return ownerModule.CorLibTypes.Int16;
			case ElementType.U2:		return ownerModule.CorLibTypes.UInt16;
			case ElementType.I4:		return ownerModule.CorLibTypes.Int32;
			case ElementType.U4:		return ownerModule.CorLibTypes.UInt32;
			case ElementType.I8:		return ownerModule.CorLibTypes.Int64;
			case ElementType.U8:		return ownerModule.CorLibTypes.UInt64;
			case ElementType.R4:		return ownerModule.CorLibTypes.Single;
			case ElementType.R8:		return ownerModule.CorLibTypes.Double;
			case ElementType.String:	return ownerModule.CorLibTypes.String;
			case ElementType.TypedByRef:return ownerModule.CorLibTypes.TypedReference;
			case ElementType.U:			return ownerModule.CorLibTypes.UIntPtr;
			case ElementType.Object:	return ownerModule.CorLibTypes.Object;
			case ElementType.Ptr:		return new PtrSig(ImportAsTypeSig(type.GetElementType(), treatAsGenericInst));
			case ElementType.ByRef:		return new ByRefSig(ImportAsTypeSig(type.GetElementType(), treatAsGenericInst));
			case ElementType.SZArray:	return new SZArraySig(ImportAsTypeSig(type.GetElementType(), treatAsGenericInst));
			case ElementType.ValueType: return new ValueTypeSig(CreateTypeRef(type));
			case ElementType.Class:		return new ClassSig(CreateTypeRef(type));
			case ElementType.Var:		return new GenericVar((uint)type.GenericParameterPosition);
			case ElementType.MVar:		return new GenericMVar((uint)type.GenericParameterPosition);

			case ElementType.I:
				fixSignature = true;	// FnPtr is mapped to System.IntPtr
				return ownerModule.CorLibTypes.IntPtr;

			case ElementType.Array:
				fixSignature = true;	// We don't know sizes and lower bounds
				return new ArraySig(ImportAsTypeSig(type.GetElementType(), treatAsGenericInst), (uint)type.GetArrayRank());

			case ElementType.GenericInst:
				var typeGenArgs = type.GetGenericArguments();
				var git = new GenericInstSig(ImportAsTypeSig(type.GetGenericTypeDefinition()) as ClassOrValueTypeSig, (uint)typeGenArgs.Length);
				foreach (var ga in typeGenArgs)
					git.GenericArguments.Add(ImportAsTypeSig(ga));
				return git;

			case ElementType.Sentinel:
			case ElementType.Pinned:
			case ElementType.FnPtr:		// mapped to System.IntPtr
			case ElementType.CModReqd:
			case ElementType.CModOpt:
			case ElementType.ValueArray:
			case ElementType.R:
			case ElementType.Internal:
			case ElementType.Module:
			case ElementType.End:
			default:
				return null;
			}
		}

		TypeRef CreateTypeRef(Type type) {
			if (!type.IsNested)
				return ownerModule.UpdateRowId(new TypeRefUser(ownerModule, type.Namespace ?? string.Empty, type.Name ?? string.Empty, CreateScopeReference(type)));
			return ownerModule.UpdateRowId(new TypeRefUser(ownerModule, string.Empty, type.Name ?? string.Empty, CreateTypeRef(type.DeclaringType)));
		}

		IResolutionScope CreateScopeReference(Type type) {
			if (type == null)
				return null;
			var asmName = type.Assembly.GetName();
			if (ownerModule.Assembly != null) {
				if (UTF8String.ToSystemStringOrEmpty(ownerModule.Assembly.Name).Equals(asmName.Name, StringComparison.OrdinalIgnoreCase)) {
					if (UTF8String.ToSystemStringOrEmpty(ownerModule.Name).Equals(type.Module.ScopeName, StringComparison.OrdinalIgnoreCase))
						return ownerModule;
					return ownerModule.UpdateRowId(new ModuleRefUser(ownerModule, type.Module.ScopeName));
				}
			}
			var pkt = asmName.GetPublicKeyToken();
			if (pkt == null || pkt.Length == 0)
				pkt = null;
			return ownerModule.UpdateRowId(new AssemblyRefUser(asmName.Name, asmName.Version, PublicKeyBase.CreatePublicKeyToken(pkt), asmName.CultureInfo.Name));
		}

		/// <summary>
		/// Imports a <see cref="Type"/> as a <see cref="ITypeDefOrRef"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <param name="requiredModifiers">A list of all required modifiers or <c>null</c></param>
		/// <param name="optionalModifiers">A list of all optional modifiers or <c>null</c></param>
		/// <returns>The imported type or <c>null</c> if <paramref name="type"/> is invalid</returns>
		public TypeSig ImportAsTypeSig(Type type, IList<Type> requiredModifiers, IList<Type> optionalModifiers) {
			return ImportAsTypeSig(type, requiredModifiers, optionalModifiers, false);
		}

		TypeSig ImportAsTypeSig(Type type, IList<Type> requiredModifiers, IList<Type> optionalModifiers, bool treatAsGenericInst) {
			if (type == null)
				return null;
			if (IsEmpty(requiredModifiers) && IsEmpty(optionalModifiers))
				return ImportAsTypeSig(type, treatAsGenericInst);

			fixSignature = true;	// Order of modifiers is unknown
			var ts = ImportAsTypeSig(type, treatAsGenericInst);

			// We don't know the original order of the modifiers.
			// Assume all required modifiers are closer to the real type.
			// Assume all modifiers should be applied in the same order as in the lists.

			if (requiredModifiers != null) {
				foreach (var modifier in requiredModifiers)
					ts = new CModReqdSig(Import(modifier), ts);
			}

			if (optionalModifiers != null) {
				foreach (var modifier in optionalModifiers)
					ts = new CModOptSig(Import(modifier), ts);
			}

			return ts;
		}

		static bool IsEmpty<T>(IList<T> list) {
			return list == null || list.Count == 0;
		}

		/// <summary>
		/// Imports a <see cref="MethodBase"/> as a <see cref="IMethod"/>. This will be either
		/// a <see cref="MemberRef"/> or a <see cref="MethodSpec"/>.
		/// </summary>
		/// <param name="methodBase">The method</param>
		/// <returns>The imported method or <c>null</c> if <paramref name="methodBase"/> is invalid
		/// or if we failed to import the method</returns>
		public IMethod Import(MethodBase methodBase) {
			return Import(methodBase, false);
		}

		/// <summary>
		/// Imports a <see cref="MethodBase"/> as a <see cref="IMethod"/>. This will be either
		/// a <see cref="MemberRef"/> or a <see cref="MethodSpec"/>.
		/// </summary>
		/// <param name="methodBase">The method</param>
		/// <param name="forceFixSignature">Always verify method signature to make sure the
		/// returned reference matches the metadata in the source assembly</param>
		/// <returns>The imported method or <c>null</c> if <paramref name="methodBase"/> is invalid
		/// or if we failed to import the method</returns>
		public IMethod Import(MethodBase methodBase, bool forceFixSignature) {
			fixSignature = false;
			if (methodBase == null)
				return null;

			if (forceFixSignature) {
				//TODO:
			}

			bool isMethodSpec = methodBase.IsGenericButNotGenericMethodDefinition();
			if (isMethodSpec) {
				var method = Import(methodBase.Module.ResolveMethod(methodBase.MetadataToken)) as IMethodDefOrRef;
				var gim = CreateGenericInstMethodSig(methodBase);
				var methodSpec = ownerModule.UpdateRowId(new MethodSpecUser(method, gim));
				if (fixSignature && !forceFixSignature) {
					//TODO:
				}
				return methodSpec;
			}
			else {
				IMemberRefParent parent;
				if (methodBase.DeclaringType == null) {
					// It's the global type. We can reference it with a ModuleRef token.
					parent = GetModuleParent(methodBase.Module);
				}
				else
					parent = Import(methodBase.DeclaringType);
				if (parent == null)
					return null;

				MethodBase origMethod;
				try {
					// Get the original method def in case the declaring type is a generic
					// type instance and the method uses at least one generic type parameter.
					origMethod = methodBase.Module.ResolveMethod(methodBase.MetadataToken);
				}
				catch (ArgumentException) {
					// Here if eg. the method was created by the runtime (eg. a multi-dimensional
					// array getter/setter method). The method token is in that case 0x06000000,
					// which is invalid.
					origMethod = methodBase;
				}

				var methodSig = CreateMethodSig(origMethod);
				var methodRef = ownerModule.UpdateRowId(new MemberRefUser(ownerModule, methodBase.Name, methodSig, parent));
				if (fixSignature && !forceFixSignature) {
					//TODO:
				}
				return methodRef;
			}
		}

		MethodSig CreateMethodSig(MethodBase mb) {
			var sig = new MethodSig(GetCallingConvention(mb));

			var mi = mb as MethodInfo;
			if (mi != null)
				sig.RetType = ImportAsTypeSig(mi.ReturnParameter, mb.DeclaringType);
			else
				sig.RetType = ownerModule.CorLibTypes.Void;

			foreach (var p in mb.GetParameters())
				sig.Params.Add(ImportAsTypeSig(p, mb.DeclaringType));

			if (mb.IsGenericMethodDefinition)
				sig.GenParamCount = (uint)mb.GetGenericArguments().Length;

			return sig;
		}

		TypeSig ImportAsTypeSig(ParameterInfo p, Type declaringType) {
			return ImportAsTypeSig(p.ParameterType, p.GetRequiredCustomModifiers(), p.GetOptionalCustomModifiers(), declaringType.MustTreatTypeAsGenericInstType(p.ParameterType));
		}

		CallingConvention GetCallingConvention(MethodBase mb) {
			CallingConvention cc = 0;

			var mbcc = mb.CallingConvention;
			if (mb.IsGenericMethodDefinition)
				cc |= CallingConvention.Generic;
			if ((mbcc & CallingConventions.HasThis) != 0)
				cc |= CallingConvention.HasThis;
			if ((mbcc & CallingConventions.ExplicitThis) != 0)
				cc |= CallingConvention.ExplicitThis;

			switch (mbcc & CallingConventions.Any) {
			case CallingConventions.Standard:
				cc |= CallingConvention.Default;
				break;

			case CallingConventions.VarArgs:
				cc |= CallingConvention.VarArg;
				break;

			case CallingConventions.Any:
			default:
				fixSignature = true;
				cc |= CallingConvention.Default;
				break;
			}

			return cc;
		}

		GenericInstMethodSig CreateGenericInstMethodSig(MethodBase mb) {
			var genMethodArgs = mb.GetGenericArguments();
			var gim = new GenericInstMethodSig(CallingConvention.GenericInst, (uint)genMethodArgs.Length);
			foreach (var gma in genMethodArgs)
				gim.GenericArguments.Add(ImportAsTypeSig(gma));
			return gim;
		}

		IMemberRefParent GetModuleParent(Module module) {
			// If we have no assembly, assume this is a netmodule in the same assembly as module
			bool isSameAssembly = ownerModule.Assembly == null ||
				UTF8String.ToSystemStringOrEmpty(ownerModule.Assembly.Name).Equals(module.Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase);
			if (!isSameAssembly)
				return null;
			return ownerModule.UpdateRowId(new ModuleRefUser(ownerModule, ownerModule.Name));
		}

		/// <summary>
		/// Imports a <see cref="FieldInfo"/> as a <see cref="MemberRef"/>
		/// </summary>
		/// <param name="fieldInfo">The field</param>
		/// <returns>The imported field or <c>null</c> if <paramref name="fieldInfo"/> is invalid
		/// or if we failed to import the field</returns>
		public MemberRef Import(FieldInfo fieldInfo) {
			return Import(fieldInfo, false);
		}

		/// <summary>
		/// Imports a <see cref="FieldInfo"/> as a <see cref="MemberRef"/>
		/// </summary>
		/// <param name="fieldInfo">The field</param>
		/// <param name="forceFixSignature">Always verify field signature to make sure the
		/// returned reference matches the metadata in the source assembly</param>
		/// <returns>The imported field or <c>null</c> if <paramref name="fieldInfo"/> is invalid
		/// or if we failed to import the field</returns>
		public MemberRef Import(FieldInfo fieldInfo, bool forceFixSignature) {
			fixSignature = false;
			if (fieldInfo == null)
				return null;

			if (forceFixSignature) {
				//TODO:
			}

			IMemberRefParent parent;
			if (fieldInfo.DeclaringType == null) {
				// It's the global type. We can reference it with a ModuleRef token.
				parent = GetModuleParent(fieldInfo.Module);
			}
			else
				parent = Import(fieldInfo.DeclaringType);
			if (parent == null)
				return null;

			FieldInfo origField;
			try {
				// Get the original field def in case the declaring type is a generic
				// type instance and the field uses a generic type parameter.
				origField = fieldInfo.Module.ResolveField(fieldInfo.MetadataToken);
			}
			catch (ArgumentException) {
				origField = fieldInfo;
			}

			var fieldSig = new FieldSig(ImportAsTypeSig(origField.FieldType));
			var fieldRef = ownerModule.UpdateRowId(new MemberRefUser(ownerModule, fieldInfo.Name, fieldSig, parent));
			if (fixSignature && !forceFixSignature) {
				//TODO:
			}
			return fieldRef;
		}

		/// <summary>
		/// Imports a <see cref="IType"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <returns>The imported type or <c>null</c></returns>
		public IType Import(IType type) {
			if (type == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			IType result;

			var td = type as TypeDef;
			if (td != null) {
				result = Import(td);
				goto exit;
			}
			var tr = type as TypeRef;
			if (tr != null) {
				result = Import(tr);
				goto exit;
			}
			var ts = type as TypeSpec;
			if (ts != null) {
				result = Import(ts);
				goto exit;
			}
			var sig = type as TypeSig;
			if (sig != null) {
				result = Import(sig);
				goto exit;
			}

			result = null;
exit:
			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="TypeDef"/> as a <see cref="TypeRef"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <returns>The imported type or <c>null</c></returns>
		public TypeRef Import(TypeDef type) {
			if (type == null)
				return null;
			if (!recursionCounter.Increment())
				return null;
			TypeRef result;

			if (type.DeclaringType != null)
				result = ownerModule.UpdateRowId(new TypeRefUser(ownerModule, type.Namespace, type.Name, Import(type.DeclaringType)));
			else
				result = ownerModule.UpdateRowId(new TypeRefUser(ownerModule, type.Namespace, type.Name, CreateScopeReference(type.DefinitionAssembly, type.OwnerModule)));

			recursionCounter.Decrement();
			return result;
		}

		IResolutionScope CreateScopeReference(IAssembly defAsm, ModuleDef defMod) {
			if (defAsm == null)
				return null;
			if (defMod != null && defAsm != null && ownerModule.Assembly != null) {
				if (UTF8String.CaseInsensitiveEquals(ownerModule.Assembly.Name, defAsm.Name)) {
					if (UTF8String.CaseInsensitiveEquals(ownerModule.Name, defMod.Name))
						return ownerModule;
					return ownerModule.UpdateRowId(new ModuleRefUser(ownerModule, defMod.Name));
				}
			}
			var pkt = PublicKeyBase.ToPublicKeyToken(defAsm.PublicKeyOrToken);
			if (PublicKeyBase.IsNullOrEmpty2(pkt))
				pkt = null;
			return ownerModule.UpdateRowId(new AssemblyRefUser(defAsm.Name, defAsm.Version, pkt, defAsm.Locale));
		}

		/// <summary>
		/// Imports a <see cref="TypeRef"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <returns>The imported type or <c>null</c></returns>
		public TypeRef Import(TypeRef type) {
			if (type == null)
				return null;
			if (!recursionCounter.Increment())
				return null;
			TypeRef result;

			var declaringType = type.DeclaringType;
			if (declaringType != null)
				result = ownerModule.UpdateRowId(new TypeRefUser(ownerModule, type.Namespace, type.Name, Import(declaringType)));
			else
				result = ownerModule.UpdateRowId(new TypeRefUser(ownerModule, type.Namespace, type.Name, CreateScopeReference(type.DefinitionAssembly, type.OwnerModule)));

			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="TypeSpec"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <returns>The imported type or <c>null</c></returns>
		public TypeSpec Import(TypeSpec type) {
			if (type == null)
				return null;
			return ownerModule.UpdateRowId(new TypeSpecUser(Import(type.TypeSig)));
		}

		/// <summary>
		/// Imports a <see cref="TypeSig"/>
		/// </summary>
		/// <param name="type">The type</param>
		/// <returns>The imported type or <c>null</c></returns>
		public TypeSig Import(TypeSig type) {
			if (type == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			TypeSig result;
			switch (type.ElementType) {
			case ElementType.Void:		result = ownerModule.CorLibTypes.Void; break;
			case ElementType.Boolean:	result = ownerModule.CorLibTypes.Boolean; break;
			case ElementType.Char:		result = ownerModule.CorLibTypes.Char; break;
			case ElementType.I1:		result = ownerModule.CorLibTypes.SByte; break;
			case ElementType.U1:		result = ownerModule.CorLibTypes.Byte; break;
			case ElementType.I2:		result = ownerModule.CorLibTypes.Int16; break;
			case ElementType.U2:		result = ownerModule.CorLibTypes.UInt16; break;
			case ElementType.I4:		result = ownerModule.CorLibTypes.Int32; break;
			case ElementType.U4:		result = ownerModule.CorLibTypes.UInt32; break;
			case ElementType.I8:		result = ownerModule.CorLibTypes.Int64; break;
			case ElementType.U8:		result = ownerModule.CorLibTypes.UInt64; break;
			case ElementType.R4:		result = ownerModule.CorLibTypes.Single; break;
			case ElementType.R8:		result = ownerModule.CorLibTypes.Double; break;
			case ElementType.String:	result = ownerModule.CorLibTypes.String; break;
			case ElementType.TypedByRef:result = ownerModule.CorLibTypes.TypedReference; break;
			case ElementType.I:			result = ownerModule.CorLibTypes.IntPtr; break;
			case ElementType.U:			result = ownerModule.CorLibTypes.UIntPtr; break;
			case ElementType.Object:	result = ownerModule.CorLibTypes.Object; break;
			case ElementType.Ptr:		result = new PtrSig(Import(type.Next)); break;
			case ElementType.ByRef:		result = new ByRefSig(Import(type.Next)); break;
			case ElementType.ValueType: result = CreateClassOrValueType((type as ClassOrValueTypeSig).TypeDefOrRef, true); break;
			case ElementType.Class:		result = CreateClassOrValueType((type as ClassOrValueTypeSig).TypeDefOrRef, false); break;
			case ElementType.Var:		result = new GenericVar((type as GenericVar).Number); break;
			case ElementType.ValueArray:result = new ValueArraySig(Import(type.Next), (type as ValueArraySig).Size); break;
			case ElementType.FnPtr:		result = new FnPtrSig(Import((type as FnPtrSig).Signature)); break;
			case ElementType.SZArray:	result = new SZArraySig(Import(type.Next)); break;
			case ElementType.MVar:		result = new GenericMVar((type as GenericMVar).Number); break;
			case ElementType.CModReqd:	result = new CModReqdSig(Import((type as ModifierSig).Modifier), Import(type.Next)); break;
			case ElementType.CModOpt:	result = new CModOptSig(Import((type as ModifierSig).Modifier), Import(type.Next)); break;
			case ElementType.Module:	result = new ModuleSig((type as ModuleSig).Index, Import(type.Next)); break;
			case ElementType.Sentinel:	result = new SentinelSig(); break;
			case ElementType.Pinned:	result = new PinnedSig(Import(type.Next)); break;

			case ElementType.Array:
				var arraySig = (ArraySig)type;
				var sizes = new List<uint>(arraySig.Sizes);
				var lbounds = new List<int>(arraySig.LowerBounds);
				result = new ArraySig(Import(type.Next), arraySig.Rank, sizes, lbounds);
				break;

			case ElementType.GenericInst:
				var gis = (GenericInstSig)type;
				result = new GenericInstSig(Import(gis.GenericType) as ClassOrValueTypeSig, gis.GenericArguments);
				break;

			case ElementType.End:
			case ElementType.R:
			case ElementType.Internal:
			default:
				result = null;
				break;
			}

			recursionCounter.Decrement();
			return result;
		}

		ITypeDefOrRef Import(ITypeDefOrRef type) {
			return (ITypeDefOrRef)Import((IType)type);
		}

		TypeSig CreateClassOrValueType(ITypeDefOrRef type, bool isValueType) {
			var corLibType = ownerModule.CorLibTypes.GetCorLibTypeSig(type);
			if (corLibType != null)
				return corLibType;

			if (isValueType)
				return new ValueTypeSig(Import(type));
			return new ClassSig(Import(type));
		}

		/// <summary>
		/// Imports a <see cref="CallingConventionSig"/>
		/// </summary>
		/// <param name="sig">The sig</param>
		/// <returns>The imported sig or <c>null</c> if input is invalid</returns>
		public CallingConventionSig Import(CallingConventionSig sig) {
			if (sig == null)
				return null;
			if (!recursionCounter.Increment())
				return null;
			CallingConventionSig result;

			var sigType = sig.GetType();
			if (sigType == typeof(MethodSig))
				result = Import((MethodSig)sig);
			else if (sigType == typeof(FieldSig))
				result = Import((FieldSig)sig);
			else if (sigType == typeof(GenericInstMethodSig))
				result = Import((GenericInstMethodSig)sig);
			else if (sigType == typeof(PropertySig))
				result = Import((PropertySig)sig);
			else if (sigType == typeof(LocalSig))
				result = Import((LocalSig)sig);
			else
				result = null;	// Should never be reached

			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="FieldSig"/>
		/// </summary>
		/// <param name="sig">The sig</param>
		/// <returns>The imported sig or <c>null</c> if input is invalid</returns>
		public FieldSig Import(FieldSig sig) {
			if (sig == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			var result = new FieldSig(sig.GetCallingConvention(), Import(sig.Type));

			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="MethodSig"/>
		/// </summary>
		/// <param name="sig">The sig</param>
		/// <returns>The imported sig or <c>null</c> if input is invalid</returns>
		public MethodSig Import(MethodSig sig) {
			if (sig == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			MethodSig result = Import(new MethodSig(sig.GetCallingConvention()), sig);

			recursionCounter.Decrement();
			return result;
		}

		T Import<T>(T sig, T old) where T : MethodBaseSig {
			sig.RetType = Import(old.RetType);
			foreach (var p in old.Params)
				sig.Params.Add(Import(p));
			sig.GenParamCount = old.GenParamCount;
			if (sig.ParamsAfterSentinel != null) {
				foreach (var p in old.ParamsAfterSentinel)
					sig.ParamsAfterSentinel.Add(Import(p));
			}
			return sig;
		}

		/// <summary>
		/// Imports a <see cref="PropertySig"/>
		/// </summary>
		/// <param name="sig">The sig</param>
		/// <returns>The imported sig or <c>null</c> if input is invalid</returns>
		public PropertySig Import(PropertySig sig) {
			if (sig == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			PropertySig result = Import(new PropertySig(sig.GetCallingConvention()), sig);

			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="LocalSig"/>
		/// </summary>
		/// <param name="sig">The sig</param>
		/// <returns>The imported sig or <c>null</c> if input is invalid</returns>
		public LocalSig Import(LocalSig sig) {
			if (sig == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			LocalSig result = new LocalSig(sig.GetCallingConvention(), (uint)sig.Locals.Count);
			foreach (var l in sig.Locals)
				result.Locals.Add(Import(l));

			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="GenericInstMethodSig"/>
		/// </summary>
		/// <param name="sig">The sig</param>
		/// <returns>The imported sig or <c>null</c> if input is invalid</returns>
		public GenericInstMethodSig Import(GenericInstMethodSig sig) {
			if (sig == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			GenericInstMethodSig result = new GenericInstMethodSig(sig.GetCallingConvention(), (uint)sig.GenericArguments.Count);
			foreach (var l in sig.GenericArguments)
				result.GenericArguments.Add(Import(l));

			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="IField"/>
		/// </summary>
		/// <param name="field">The field</param>
		/// <returns>The imported type or <c>null</c> if <paramref name="field"/> is invalid</returns>
		public IField Import(IField field) {
			if (field == null)
				return null;
			if (!recursionCounter.Increment())
				return null;
			IField result;

			var fd = field as FieldDef;
			if (fd != null) {
				result = Import(fd);
				goto exit;
			}
			var mr = field as MemberRef;
			if (mr != null) {
				result = Import(mr);
				goto exit;
			}

			result = null;
exit:
			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="IMethod"/>
		/// </summary>
		/// <param name="method">The method</param>
		/// <returns>The imported method or <c>null</c> if <paramref name="method"/> is invalid</returns>
		public IMethod Import(IMethod method) {
			if (method == null)
				return null;
			if (!recursionCounter.Increment())
				return null;
			IMethod result;

			var md = method as MethodDef;
			if (md != null) {
				result = Import(md);
				goto exit;
			}
			var ms = method as MethodSpec;
			if (ms != null) {
				result = Import(ms);
				goto exit;
			}
			var mr = method as MemberRef;
			if (mr != null) {
				result = Import(mr);
				goto exit;
			}

			result = null;
exit:
			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="FieldDef"/> as a <see cref="MemberRef"/>
		/// </summary>
		/// <param name="field">The field</param>
		/// <returns>The imported type or <c>null</c> if <paramref name="field"/> is invalid</returns>
		public MemberRef Import(FieldDef field) {
			if (field == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			MemberRef result = ownerModule.UpdateRowId(new MemberRefUser(ownerModule, field.Name));
			result.Signature = Import(field.Signature);
			result.Class = ImportParent(field.DeclaringType);

			recursionCounter.Decrement();
			return result;
		}

		IMemberRefParent ImportParent(TypeDef type) {
			if (type == null)
				return null;
			if (type.IsGlobalModuleType) {
				var om = type.OwnerModule;
				return ownerModule.UpdateRowId(new ModuleRefUser(ownerModule, om == null ? null : om.Name));
			}
			return Import(type);
		}

		/// <summary>
		/// Imports a <see cref="MethodDef"/> as a <see cref="MemberRef"/>
		/// </summary>
		/// <param name="method">The method</param>
		/// <returns>The imported method or <c>null</c> if <paramref name="method"/> is invalid</returns>
		public MemberRef Import(MethodDef method) {
			if (method == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			MemberRef result = ownerModule.UpdateRowId(new MemberRefUser(ownerModule, method.Name));
			result.Signature = Import(method.Signature);
			result.Class = ImportParent(method.DeclaringType);

			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="MethodSpec"/>
		/// </summary>
		/// <param name="method">The method</param>
		/// <returns>The imported method or <c>null</c> if <paramref name="method"/> is invalid</returns>
		public MethodSpec Import(MethodSpec method) {
			if (method == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			MethodSpec result = ownerModule.UpdateRowId(new MethodSpecUser((IMethodDefOrRef)Import(method.Method)));
			result.Instantiation = Import(method.Instantiation);

			recursionCounter.Decrement();
			return result;
		}

		/// <summary>
		/// Imports a <see cref="MemberRef"/>
		/// </summary>
		/// <param name="memberRef">The member ref</param>
		/// <returns>The imported member ref or <c>null</c> if <paramref name="memberRef"/> is invalid</returns>
		public MemberRef Import(MemberRef memberRef) {
			if (memberRef == null)
				return null;
			if (!recursionCounter.Increment())
				return null;

			MemberRef result = ownerModule.UpdateRowId(new MemberRefUser(ownerModule, memberRef.Name));
			result.Signature = Import(memberRef.Signature);
			result.Class = Import(memberRef.Class);
			if (result.Class == null)	// Will be null if memberRef.Class is null or a MethodDef
				result = null;

			recursionCounter.Decrement();
			return result;
		}

		IMemberRefParent Import(IMemberRefParent parent) {
			var tdr = parent as ITypeDefOrRef;
			if (tdr != null) {
				var td = tdr as TypeDef;
				if (td != null && td.IsGlobalModuleType) {
					var om = td.OwnerModule;
					return ownerModule.UpdateRowId(new ModuleRefUser(ownerModule, om == null ? null : om.Name));
				}
				return Import(tdr);
			}

			var modRef = parent as ModuleRef;
			if (modRef != null)
				return ownerModule.UpdateRowId(new ModuleRefUser(ownerModule, modRef.Name));

			var method = parent as MethodDef;
			if (method != null) {
				var dt = method.DeclaringType;
				return dt == null || dt.OwnerModule != ownerModule ? null : method;
			}

			return null;
		}
	}
}