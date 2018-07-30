using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.VFX;

namespace UnityEditor.VFX.Block
{
    class PositionSequentialVariantProvider : IVariantProvider
    {
        public Dictionary<string, object[]> variants
        {
            get
            {
                return new Dictionary<string, object[]>
                {
                    { "shape", Enum.GetValues(typeof(PositionSequential.SequentialShape)).Cast<object>().ToArray() }
                };
            }
        }
    }

    [VFXInfo(category = "Position", variantProvider = typeof(PositionSequentialVariantProvider), experimental = true)]
    class PositionSequential : VFXBlock
    {
        public enum SequentialShape
        {
            Line,
            Circle,
            ThreeDimensional,
        }

        public enum IndexMode
        {
            ParticleID,
            Custom
        }

        public enum TargetPositionMode
        {
            None,
            Same,
            Next
        }

        [SerializeField, VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector)]
        [Tooltip(@"Shape used for this sequence")]
        protected SequentialShape shape = SequentialShape.Line;

        [SerializeField, VFXSetting]
        [Tooltip(@"Index used for fetching progression in this sequence:
- Particle ID
- Custom provided index")]
        protected IndexMode index = IndexMode.ParticleID;

        [SerializeField, VFXSetting]
        [Tooltip(@"Whether targetPosition Attribute should be computed:
- None : no modification
- Same: computed at same index as position
- Next: computed at index + 1")]
        protected TargetPositionMode TargetPosition = TargetPositionMode.Next;

        public override string name { get { return string.Format("Position : Sequential ({0})", shape); } }
        public override VFXContextType compatibleContexts { get { return VFXContextType.kInitAndUpdateAndOutput; } }
        public override VFXDataType compatibleData { get { return VFXDataType.kParticle; } }

        public class InputProperties
        {
        }

        public class InputPropertiesCustomIndex
        {
            [Tooltip("Index used to sample the sequential distribution")]
            public uint Index = 0;
        }

        public class InputPropertiesLine
        {
            [Tooltip("Element count used to loop over the sequence")]
            public uint Count = 64;
            [Tooltip("Start Position")]
            public Position Start = Position.defaultValue;
            [Tooltip("End Position")]
            public Position End = new Position() { position = new Vector3(1, 0, 0) };
        }

        public class InputPropertiesCircle
        {
            [Tooltip("Element count used to loop over the sequence")]
            public uint Count = 64;
            [Tooltip("Center of the circle")]
            public Position Center = Position.defaultValue;
            [Tooltip("Rotation Axis")]
            public DirectionType Normal = new DirectionType() { direction = Vector3.forward };
            [Tooltip("Start Angle (Midnight direction)")]
            public DirectionType Up = new DirectionType() { direction = Vector3.up };
            [Tooltip("Radius of the circle")]
            public float Radius = 1.0f;
        }

        public class InputPropertiesThreeDimensional
        {
            public Position Origin = Position.defaultValue;

            public Vector3 AxisX = Vector3.right;
            public Vector3 AxisY = Vector3.up;
            public Vector3 AxisZ = Vector3.forward;

            [Tooltip("Element X count used to loop over the sequence")]
            public uint CountX = 8;
            [Tooltip("Element Y count used to loop over the sequence")]
            public uint CountY = 8;
            [Tooltip("Element Z count used to loop over the sequence")]
            public uint CountZ = 8;
        }

        protected override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get
            {
                var commonProperties = PropertiesFromType("InputProperties");

                switch (shape)
                {
                    case SequentialShape.Line: commonProperties = commonProperties.Concat(PropertiesFromType("InputPropertiesLine")); break;
                    case SequentialShape.Circle: commonProperties = commonProperties.Concat(PropertiesFromType("InputPropertiesCircle")); break;
                    case SequentialShape.ThreeDimensional: commonProperties = commonProperties.Concat(PropertiesFromType("InputPropertiesThreeDimensional")); break;
                }

                if (index == IndexMode.Custom)
                    commonProperties = commonProperties.Concat(PropertiesFromType("InputPropertiesCustomIndex"));

                return commonProperties;
            }
        }

        public override IEnumerable<VFXAttributeInfo> attributes
        {
            get
            {
                if (index == IndexMode.ParticleID)
                    yield return new VFXAttributeInfo(VFXAttribute.ParticleId, VFXAttributeMode.Read);

                yield return new VFXAttributeInfo(VFXAttribute.Position, VFXAttributeMode.ReadWrite);

                if (TargetPosition != TargetPositionMode.None)
                    yield return new VFXAttributeInfo(VFXAttribute.TargetPosition, VFXAttributeMode.ReadWrite);
            }
        }

        private VFXExpression GetPositionFromIndex(VFXExpression indexExpr, IEnumerable<VFXNamedExpression> expressions)
        {
            if (shape == SequentialShape.Line)
            {
                var start = expressions.First(o => o.name == "Start").exp;
                var end = expressions.First(o => o.name == "End").exp;
                var count = expressions.First(o => o.name == "Count").exp;
                return VFXOperatorUtility.SequentialLine(start, end, indexExpr, count);
            }
            else if (shape == SequentialShape.Circle)
            {
                var center = expressions.First(o => o.name == "Center").exp;
                var normal = expressions.First(o => o.name == "Normal").exp;
                var up = expressions.First(o => o.name == "Up").exp;
                var radius = expressions.First(o => o.name == "Radius").exp;
                var count = expressions.First(o => o.name == "Count").exp;
                return VFXOperatorUtility.SequentialCircle(center, radius, normal, up, indexExpr, count);
            }
            else if (shape == SequentialShape.ThreeDimensional)
            {
                var origin = expressions.First(o => o.name == "Origin").exp;
                var axisX = expressions.First(o => o.name == "AxisX").exp;
                var axisY = expressions.First(o => o.name == "AxisY").exp;
                var axisZ = expressions.First(o => o.name == "AxisZ").exp;
                var countX = expressions.First(o => o.name == "CountX").exp;
                var countY = expressions.First(o => o.name == "CountY").exp;
                var countZ = expressions.First(o => o.name == "CountZ").exp;
                return VFXOperatorUtility.Sequential3D(origin, axisX, axisY, axisZ, indexExpr, countX, countY, countZ);
            }
            throw new NotImplementedException();
        }

        private static readonly string s_computedPosition = "computedPosition";
        private static readonly string s_computedTargetPosition = "computedTargetPosition";

        public override IEnumerable<VFXNamedExpression> parameters
        {
            get
            {
                var expressions = GetExpressionsFromSlots(this);
                var indexExpr = (index == IndexMode.ParticleID) ? new VFXAttributeExpression(VFXAttribute.ParticleId) : expressions.First(o => o.name == "Index").exp;
                var positionExpr = GetPositionFromIndex(indexExpr, expressions);

                yield return new VFXNamedExpression(positionExpr, s_computedPosition);

                if (TargetPosition != TargetPositionMode.None)
                {
                    if (TargetPosition == TargetPositionMode.Next)
                    {
                        indexExpr = indexExpr + VFXOperatorUtility.OneExpression[VFXValueType.Uint32];
                        yield return new VFXNamedExpression(GetPositionFromIndex(indexExpr, expressions), s_computedTargetPosition);
                    }
                    else
                    {
                        yield return new VFXNamedExpression(positionExpr, s_computedTargetPosition);
                    }
                }
            }
        }

        public override string source
        {
            get
            {
                var source = string.Format("position += {0};", s_computedPosition);
                if (TargetPosition != TargetPositionMode.None)
                {
                    source += string.Format("\ntargetPosition += {0};", s_computedTargetPosition);
                }
                return source;
            }
        }
    }
}
