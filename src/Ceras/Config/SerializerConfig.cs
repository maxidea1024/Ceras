﻿namespace Ceras
{
	using Ceras.Formatters;
	using Helpers;
	using Resolvers;
	using System;
	using System.Collections.Generic;

	/// <summary>
	/// Allows detailed configuration of the <see cref="CerasSerializer"/>. Advanced options can be found inside <see cref="Advanced"/>
	/// </summary>
	public class SerializerConfig : IAdvancedConfigOptions, ISizeLimitsConfig
	{
		bool _isSealed; // todo
		internal bool IsSealed => _isSealed;
		internal void Seal() => _isSealed = true;


		#region Basic Settings

		/// <summary>
		/// If you want to, you can add all the types you want to serialize to this collection.
		/// When you add at least one Type to this list, Ceras will run in "sealed mode", which does 2 different things:
		/// 
		/// <para>
		/// 1.) It improves performance.
		/// Usually Ceras already only writes the type-name of an object when absolutely necessary. But sometimes you might have 'object' or 'interface' fields, in which case there's simply no way but to embed the type information. And this is where KnownTypes helps: Since the the types (and their order) are known, Ceras can write just a "TypeId" instead of the full name. This can save *a lot* of space and also increases performance (since less data has to be written).
		/// </para>
		///
		/// <para>
		/// 2.) It protects against bugs and exploits.
		/// When a new type (one that is not in the KnownTypes list) is encountered while reading/writing an exception is thrown!
		/// You can be sure that you will never accidentally drag some object that you didn't intend to serialize (protecting against bugs).
		/// It also prevents exploits when using Ceras to send objects over the network, because an attacker can not inject new object-types into your data (which, depending on what you do, could be *really* bad).
		/// </para>
		/// 
		/// By default this prevents new types being added dynamically, but you can change this setting in <see cref="Advanced.SealTypesWhenUsingKnownTypes"/>.
		/// 
		/// <para>Ceras refers to the types by their index in this list!</para>
		/// So for deserialization the same types must be present in the same order! You can however have new types at the end of the list (so you can still load old data, as long as the types that an object was saved with are still present at the expected indices)
		/// 
		/// See the tutorial for more information.
		/// </summary>
		public List<Type> KnownTypes { get; internal set; } = new List<Type>();

		/// <summary>
		/// If your object implement IExternalRootObject they are written as their external ID, so at deserialization-time you need to provide a resolver for Ceras so it can get back the Objects from their IDs.
		/// When would you use this?
		/// There's a lot of really interesting use cases for this, be sure to read the tutorial section 'GameDatabase' even if you're not making a game.
		/// <para>Default: null</para>
		/// </summary>
		public IExternalObjectResolver ExternalObjectResolver { get; set; }

		/// <summary>
		/// If one of the objects in the graph implements IExternalRootObject, Ceras will only write its ID and then call this function. 
		/// That means this external object for which only the ID was written, was not serialized itself. But often you want to sort of "collect" all the elements
		/// that belong into an object-graph and save them at the same time. That's when you'd use this callback. 
		/// Make sure to read the 'GameDatabase' example in the tutorial even if you're not making a game.
		/// <para>Default: null</para>
		/// </summary>
		public Action<IExternalRootObject> OnExternalObject { get; set; } = null;

		/// <summary>
		/// A list of callbacks that Ceras calls when it needs a formatter for some type. The given methods in this list will be tried one after another until one of them returns a IFormatter instance. If all of them return null (or the list is empty) then Ceras will continue as usual, trying the built-in formatters.
		/// </summary>
		public List<FormatterResolverCallback> OnResolveFormatter { get; } = new List<FormatterResolverCallback>();

		/// <summary>
		/// Whether or not to handle object references.
		/// This feature will correctly handle circular references (which would otherwise just crash with a StackOverflowException), but comes at a (very) small performance cost; so turn it off if you know that you won't need it.
		/// <para>Default: true</para>
		/// </summary>
		public bool PreserveReferences { get; set; } = true;

		/// <summary>
		/// If true, Ceras will skip fields with the '[System.NonSerialized]' attribute
		/// <para>Default: true</para>
		/// </summary>
		public bool RespectNonSerializedAttribute { get; set; } = true;

		/// <summary>
		/// Sometimes you want to persist objects even while they evolve (fields being added, removed, renamed).
		/// Type changes are not supported (yet, nobody has requested it so far).
		/// Check out the tutorial for more information (and a way to deal with changing types)
		/// <para>Default: Disabled</para>
		/// </summary>
		public VersionTolerance VersionTolerance { get; set; } = VersionTolerance.Disabled;

		/// <summary>
		/// If all the other things (ShouldSerializeMember / Attributes) don't produce a decision, then this setting is used to determine if a member should be included.
		/// By default only public fields are serialized. ReadonlyHandling is a separate option found inside <see cref="Advanced"/>
		/// <para>Default: PublicFields</para>
		/// </summary>
		public TargetMember DefaultTargets { get; set; } = TargetMember.PublicFields;

		#endregion


		#region Type Configuration

		Dictionary<Type, TypeConfig> _configEntries = new Dictionary<Type, TypeConfig>();

		// Get a TypeConfig without calling 'OnConfigNewType'
		TypeConfig GetTypeConfigForConfiguration(Type type)
		{
			if (_configEntries.TryGetValue(type, out var typeConfig))
				return typeConfig;

			typeConfig = (TypeConfig)Activator.CreateInstance(typeof(TypeConfig<>).MakeGenericType(type), this);
			_configEntries.Add(type, typeConfig);

			return typeConfig;
		}

		// Get a TypeConfig for usage, meaning if by now the type has not been configured then
		// use 'OnConfigNewType' as a last chance (or if no callback is set just use the defaults)
		internal TypeConfig GetTypeConfig(Type type)
		{
			if (_configEntries.TryGetValue(type, out var typeConfig))
				return typeConfig;
			
			typeConfig = (TypeConfig)Activator.CreateInstance(typeof(TypeConfig<>).MakeGenericType(type), this);

			// Let the user handle it
			OnConfigNewType?.Invoke((TypeConfig)typeConfig);

			_configEntries.Add(type, typeConfig);
			return typeConfig;
		}

		
		/// <summary>
		/// Use the generic version of <see cref="ConfigType{T}"/> for a much easier API.
		/// <para>
		/// This overload should only be used if you actually don't know the type in advance (for example when dealing with a private type in another assembly)
		/// </para>
		/// </summary>
		public TypeConfig ConfigType(Type type) => GetTypeConfigForConfiguration(type);
		/// <summary>
		/// Use this when you want to configure types directly (instead of through attributes, or <see cref="OnConfigNewType"/>). Any changes you make using this method will override any settings applied through attributes on the type.
		/// </summary>
		public TypeConfig<T> ConfigType<T>() => (TypeConfig<T>)GetTypeConfigForConfiguration(typeof(T));


		/// <summary>
		/// Usually you would just put attributes (like <see cref="MemberConfigAttribute"/>) on your types to define how they're serialized. But sometimes you want to configure some types that you don't control (like types from some external library you're using). In that case you'd use <see cref="ConfigType{T}"/>. But sometimes even that doesn't work, for example when some types are private, or too numerous, or generic (so they don't even exist as "closed" / specific types yet); so when you're in a situation like that, you'd use this <see cref="OnConfigNewType"/> to configure a type right when it's used.
		/// <para>
		/// Keep in mind that this callback will only be called when Ceras encounters it for the first time. 
		/// That means it will not get called for any type that you have already configured using <see cref="ConfigType{T}"/>!
		/// </para>
		/// </summary>
		public Action<TypeConfig> OnConfigNewType
		{
			get => _onConfigNewType;
			set
			{
				if (_onConfigNewType == null)
					_onConfigNewType = value;
				else
					throw new InvalidOperationException(nameof(OnConfigNewType) + " is already set. Multiple type configuration callbacks would overwrite each others changes, you must collect all the callbacks into one function to maintain detailed control over how each Type gets configured.");
			}
		}
		Action<TypeConfig> _onConfigNewType;

		#endregion



		/// <summary>
		/// Advanced options. In here is everything that is very rarely used, dangerous, or otherwise special. 
		/// </summary>
		public IAdvancedConfigOptions Advanced => this;

		ISizeLimitsConfig IAdvancedConfigOptions.SizeLimits => this;
		uint ISizeLimitsConfig.MaxStringLength { get; set; } = uint.MaxValue;
		uint ISizeLimitsConfig.MaxArraySize { get; set; } = uint.MaxValue;
		uint ISizeLimitsConfig.MaxByteArraySize { get; set; } = uint.MaxValue;
		uint ISizeLimitsConfig.MaxCollectionSize { get; set; } = uint.MaxValue;

		Action<object> IAdvancedConfigOptions.DiscardObjectMethod { get; set; } = null;
		ReadonlyFieldHandling IAdvancedConfigOptions.ReadonlyFieldHandling { get; set; } = ReadonlyFieldHandling.ExcludeFromSerialization;
		bool IAdvancedConfigOptions.EmbedChecksum { get; set; } = false;
		bool IAdvancedConfigOptions.PersistTypeCache { get; set; } = false;
		bool IAdvancedConfigOptions.SealTypesWhenUsingKnownTypes { get; set; } = true;
		bool IAdvancedConfigOptions.SkipCompilerGeneratedFields { get; set; } = true;
		ITypeBinder IAdvancedConfigOptions.TypeBinder { get; set; } = null;
		DelegateSerializationMode IAdvancedConfigOptions.DelegateSerialization { get; set; } = DelegateSerializationMode.Off;
		public bool UseReinterpretFormatter { get; set; } = true;
	}


	public interface IAdvancedConfigOptions
	{
		/// <summary>
		/// Set this to a function you provide. Ceras will call it when an object instance is no longer needed.
		/// For example you want to populate an existing object with data, and one of the fields already has a value (a left-over from the last time it was used),
		/// but the current data says that the field should be 'null'. That's when Ceras will call this this method so you can recycle the object (maybe return it to your object-pool)
		/// </summary>
		Action<object> DiscardObjectMethod { get; set; }

		/// <summary>
		/// Explaining this setting here would take too much space, check out the tutorial section for details.
		/// <para>Default: Off</para>
		/// </summary>
		ReadonlyFieldHandling ReadonlyFieldHandling { get; set; }

		/// <summary>
		/// Embed protocol/serializer checksum at the start of any serialized data, and read it back when deserializing to make sure we're not reading incompatible data on accident.
		/// Intended to be used when writing to files, for networking this should not be used (since it would prefix every message with the serializer-checksum which makes no sense)
		/// <para>Default: false</para>
		/// </summary>
		bool EmbedChecksum { get; set; }

		/// <summary>
		/// Determines whether to keep Type-To-Id maps after serialization/deserialization.
		/// This is ***ONLY*** intended for networking, where the deserializer keeps the state as well, and all serialized data is ephemeral (not saved to anywhere)
		/// This will likely save a huge amount of memory and cpu cycles over the lifespan of a network-session, because it will serialize type-information only once.
		/// 
		/// If the serializer is used as a network protocol serializer, this option should definitely be turned on!
		/// Don't use this when serializing to anything persistent (files, database, ...) as you cannot deserialize any data if the deserializer type-cache is not in **EXACTLY**
		/// the same configuration as it (unless you really know exactly what you're doing)
		/// <para>Default: false</para>
		/// </summary>
		bool PersistTypeCache { get; set; }

		/// <summary>
		/// This setting is only used when KnownTypes is used (has >0 entries).
		/// When set to true, and a new Type (so a Type that is not contained in KnownTypes) is encountered in either serialization or deserialization, an exception is thrown.
		/// 
		/// <para>!! Defaults to true to protect against exploits and bugs.</para>
		/// <para>!! Don't disable this unless you know what you're doing.</para>
		///
		/// If you use KnownTypes you're most likely using Ceras in a network-scenario.
		/// If you then turn off this setting, you're basically allowing the other side (client or server) to construct whatever object they want on your side (which is known to be a huge attack vector for networked software).
		///
		/// It also protects against bugs by ensuring you are 100% aware of all the types that get serialized.
		/// You could easily end up including stuff like passwords, usernames, access-keys, ... completely by accident. 
		/// 
		/// The idea is that when someone uses KnownTypes, they have a fixed list of types they want to serialize (to minimize overhead from serializing type names initially),
		/// which is usually done in networking scenarios;
		/// While working on a project you might add more types or add new fields or things like that, and a common mistake is accidentally adding a new type (or even whole graph!)
		/// to the object graph that was not intended; which is obviously extremely problematic (super risky if sensitive stuff gets suddenly dragged into the serialization)
		/// <para>Default: true</para>
		/// </summary>
		bool SealTypesWhenUsingKnownTypes { get; set; }

		/// <summary>
		/// !! Important:
		/// You may believe you know what you're doing when including things compiler-generated fields, but there are tons of other problems you most likely didn't even realize unless you've read the github issue here: https://github.com/rikimaru0345/Ceras/issues/11. 
		/// 
		/// Hint: You may end up including all sorts of stuff like enumerator statemachines, delegates, remanants of 'dynamic' objects, ...
		/// So here's your warning: Don't set this to false unless you know what you're doing.
		/// 
		/// This defaults to true, which means that fields marked as [CompilerGenerated] are skipped without asking your 'ShouldSerializeMember' function (if you have set one).
		/// For 99% of all use cases this is exactly what you want. For more information read the 'readonly properties' section in the tutorial.
		/// <para>Default: true</para>
		/// </summary>
		bool SkipCompilerGeneratedFields { get; set; }

		/// <summary>
		/// A TypeBinder simply converts a 'Type' to a string and back.
		/// It's easy and really useful to provide your own type binder in many situations.
		/// <para>Examples:</para>
		/// <para>- Mapping server objects to client objects</para>
		/// <para>- Shortening / abbreviating type-names to save space and performance</para>
		/// The default type binder (NaiveTypeBinder) simply uses '.FullName'
		/// See the readme on github for more information.
		/// </summary>
		ITypeBinder TypeBinder { get; set; }

		/// <summary>
		/// Protect against malicious input while deserializing by setting size limits for strings, arrays, and collections
		/// </summary>
		ISizeLimitsConfig SizeLimits { get; }

		/// <summary>
		/// This setting allows Ceras to serialize delegates. In order to make it as safe as possible, set it to the lowest setting that works for you.
		/// 'AllowStatic' will only allow serialization of delegates that point to static methods (so no instances / targets).
		/// While 'AllowInstance' will also allow serialization of instance-methods, meaning that the target object will be "pulled into" the serialization as well.
		/// <para>Default: Off</para>
		/// </summary>
		DelegateSerializationMode DelegateSerialization { get; set; }

		/// <summary>
		/// Use a special, extremely fast formatter when possible.
		/// This formatter re-interprets the buffer pointer so the value(s) can be written/read directly.
		/// Works with all value-types (structs), including generics, as long as the type contains no managed object references.
		/// Supports individual objects as well as arrays.
		/// All data is written in the processor-native endianness.
		/// <para>Default: true</para>
		/// </summary>
		bool UseReinterpretFormatter { get; set; }
	}


	public interface ISizeLimitsConfig
	{
		/// <summary>
		/// Maximum string length
		/// </summary>
		uint MaxStringLength { get; set; }
		/// <summary>
		/// Maximum size of any byte[] members
		/// </summary>
		uint MaxArraySize { get; set; }
		/// <summary>
		/// Maximum size of any array members (except byte arrays)
		/// </summary>
		uint MaxByteArraySize { get; set; }
		/// <summary>
		/// Maximum number of elements to read for any collection (everything that implements ICollection, so List, Dictionary, ...)
		/// </summary>
		uint MaxCollectionSize { get; set; }
	}

	public enum DelegateSerializationMode
	{
		/// <summary>
		/// Throw an exception when trying to serialize a delegate type
		/// </summary>
		Off,
		/// <summary>
		/// Allow delegates as long as they point to static methods
		/// </summary>
		AllowStatic,
		/// <summary>
		/// Allow delegates even when they include an object reference (that will get serialized as well)
		/// </summary>
		AllowInstance,
	}


	/// <summary>
	/// Options how Ceras handles readonly fields. Check the description of each entry.
	/// </summary>
	public enum ReadonlyFieldHandling
	{
		/// <summary>
		/// This is the default, Ceras will not serialize/deserialize readonly fields.
		/// </summary>
		ExcludeFromSerialization = 0,

		/// <summary>
		/// Serialize readonly fields normally, but at deserialization time it is expected that an object is already present (so Ceras does not have to change the readonly-field), however Ceras will deserialize the content of the object inside the readonly field.
		/// <para>
		/// Example: An object that has a 'readonly Settings MySettings;' field. Ceras will not change the field itself, but it will serialize and deserialize all the settings values inside.
		/// That's what you often want. But it obviously requires that you either provide an object that already exists (meaning you're using the <see cref="CerasSerializer.Deserialize{T}(ref T, byte[])"/> overload that takes an existing object to overwrite); or that the containing object will put an instance into the readonly field in its constructor.
		///</para>
		/// If the object in the readonly field itself does not match the expected value an exception is thrown.
		/// Keep in mind that this mode will obviously never work with value-types (int, structs, ...), in that case simply use <see cref="ForcedOverwrite"/>.
		/// </summary>
		Members = 1,

		/// <summary>
		/// This mode means pretty much "treat readonly fields exactly the same as normal fields". But since readonly fields can't normally be changed outside the constructor of the object Ceras will use reflection to forcefully overwrite the object field.
		/// </summary>
		ForcedOverwrite = 2,
	}

	public enum VersionTolerance
	{
		Disabled,
		AutomaticEmbedded,
	}

	public delegate IFormatter FormatterResolverCallback(CerasSerializer ceras, Type typeToBeFormatted);

}